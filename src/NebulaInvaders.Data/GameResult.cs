namespace NebulaInvaders.Data;

/// <summary>
/// Resultado inmutable que viaja desde la interfaz a la cola JSON y después a SQL.
/// Id se conserva en cada reintento; Completed solo indica victoria en el quinto nivel.
/// </summary>
public sealed record GameResult(
    Guid Id,
    string PlayerName,
    int Score,
    int LevelReached,
    bool Completed,
    int DurationSeconds,
    string ControlMode,
    DateTime StartedUtc,
    DateTime EndedUtc);

/// <summary>Proyección de las columnas que necesita el ranking; las fechas se almacenan en UTC.</summary>
public sealed record LeaderboardEntry(
    string PlayerName,
    int Score,
    int LevelReached,
    bool Completed,
    DateTime EndedUtc);

/// <summary>Aplica las mismas reglas antes de escribir en disco o SQL para no encolar registros imposibles.</summary>
internal static class GameResultValidator
{
    internal static void Validate(GameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Id == Guid.Empty)
            throw new ArgumentException("La partida necesita un identificador único.", nameof(result));
        ValidateText(result.PlayerName, 32, nameof(result.PlayerName));
        ValidateText(result.ControlMode, 24, nameof(result.ControlMode));
        if (result.Score < 0)
            throw new ArgumentOutOfRangeException(nameof(result.Score), "La puntuación no puede ser negativa.");
        if (result.LevelReached is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(result.LevelReached), "El nivel debe estar entre 1 y 5.");
        if (result.Completed && result.LevelReached != 5)
            throw new ArgumentException("Una victoria debe alcanzar el nivel 5.", nameof(result));
        if (result.DurationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(result.DurationSeconds));
        if (result.StartedUtc.Kind != DateTimeKind.Utc || result.EndedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Las fechas de la partida deben expresarse en UTC.", nameof(result));
        if (result.EndedUtc < result.StartedUtc)
            throw new ArgumentException("La fecha final no puede preceder al inicio.", nameof(result));
    }

    private static void ValidateText(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value != value.Trim() || value.Any(char.IsControl))
            throw new ArgumentException($"El texto debe tener de 1 a {maxLength} caracteres, sin controles ni espacios en los extremos.", name);
    }
}
