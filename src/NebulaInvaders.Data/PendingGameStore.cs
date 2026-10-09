using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NebulaInvaders.Data;

/// <summary>
/// Cola local: primero se conserva el resultado en JSON y después se intenta enviarlo a SQL.
/// Un registro se retira solo tras confirmar la inserción; su UUID permite reintentar sin duplicados.
/// Las escrituras sustituyen el archivo completo de forma atómica. Debe haber un proceso por cola.
/// </summary>
public sealed class PendingGameStore
{
    // Instancias de esta clase que apuntan al mismo archivo comparten el mismo semáforo.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public PendingGameStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _gate = Gates.GetOrAdd(_path, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>Valida y añade un resultado; repetir exactamente el mismo UUID no agrega otra copia.</summary>
    public async Task EnqueueAsync(GameResult result, CancellationToken cancellationToken = default)
    {
        GameResultValidator.Validate(result);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var existing = items.Find(item => item.Id == result.Id);
            if (existing is not null)
            {
                if (existing != result)
                    throw new InvalidDataException("El identificador de partida ya existe con datos diferentes.");
                return;
            }
            items.Add(result);
            await WriteAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Lee cuántos resultados siguen pendientes, conservando la exclusión entre escritores.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Count; }
        finally { _gate.Release(); }
    }

    /// <summary>Envía registros en orden. Si falla SQL o disco, propaga el error y conserva lo no confirmado.</summary>
    public async Task<int> FlushAsync(SqlGameRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var saved = 0;
            while (items.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await repository.SaveAsync(items[0], cancellationToken).ConfigureAwait(false);
                items.RemoveAt(0);
                // Si falla la escritura local, el archivo anterior todavía contiene el UUID.
                // Reenviarlo es seguro: SQL reconoce que esa partida ya existe.
                await WriteAsync(items, cancellationToken).ConfigureAwait(false);
                saved++;
            }
            return saved;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Un archivo ausente es una cola vacía; uno dañado genera error y nunca se sobrescribe.</summary>
    private async Task<List<GameResult>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var items = await JsonSerializer.DeserializeAsync<List<GameResult>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("La lista de partidas pendientes es nula.");
            var ids = new HashSet<Guid>();
            foreach (var item in items)
            {
                GameResultValidator.Validate(item);
                if (!ids.Add(item.Id))
                    throw new InvalidDataException("La lista de partidas pendientes contiene identificadores repetidos.");
            }
            return items;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException($"No se puede leer la cola de partidas en '{_path}'. Se conserva intacta para recuperarla manualmente.", ex);
        }
    }

    /// <summary>Escribe y vacía buffers en un temporal antes de sustituir el JSON definitivo.</summary>
    private async Task WriteAsync(List<GameResult> items, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // El temporal está en el mismo directorio: el cambio de nombre evita exponer un JSON parcial.
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { /* Un temporal sobrante no afecta al archivo válido de la cola. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
