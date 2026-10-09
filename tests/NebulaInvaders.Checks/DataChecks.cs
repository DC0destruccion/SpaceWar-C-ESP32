using Microsoft.Data.SqlClient;
using NebulaInvaders.Data;

namespace NebulaInvaders.Checks;

/// <summary>Prueba la cola en una carpeta temporal y reserva SQL real para una opción explícita.</summary>
internal static class DataChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "NebulaInvaders-DataChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "pending.json");
            var store = new PendingGameStore(path);
            Assert(await store.CountAsync() == 0, "A new outbox is empty.");
            var sample = Sample();
            await store.EnqueueAsync(sample);
            await store.EnqueueAsync(sample);
            Assert(await new PendingGameStore(path).CountAsync() == 1, "Reopening keeps one copy of the same Id.");
            await ExpectAsync<InvalidDataException>(() => store.EnqueueAsync(sample with { Score = 999 }));
            Assert(await store.CountAsync() == 1, "Conflicting duplicate data leaves the original intact.");

            await ExpectAsync<ArgumentException>(() => store.EnqueueAsync(sample with { PlayerName = "  " }));
            await ExpectAsync<ArgumentOutOfRangeException>(() => store.EnqueueAsync(sample with { LevelReached = 6 }));
            await ExpectAsync<ArgumentOutOfRangeException>(() => store.EnqueueAsync(sample with { Score = -1 }));
            await ExpectAsync<ArgumentException>(() => store.EnqueueAsync(sample with { Completed = true, LevelReached = 1 }));
            await ExpectAsync<ArgumentException>(() => store.EnqueueAsync(sample with { EndedUtc = sample.StartedUtc.AddSeconds(-1) }));
            await ExpectAsync<ArgumentException>(() => store.EnqueueAsync(sample with { StartedUtc = DateTime.SpecifyKind(sample.StartedUtc, DateTimeKind.Unspecified) }));

            // Varias instancias para el mismo archivo comparten semáforo: no deben perder registros.
            await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
                new PendingGameStore(path).EnqueueAsync(Sample() with { Score = i })));
            Assert(await store.CountAsync() == 25, "Concurrent enqueue retains all unique games.");

            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                await ExpectAsync<OperationCanceledException>(() => store.EnqueueAsync(Sample(), canceled.Token));
            }
            Assert(await store.CountAsync() == 25, "Canceled enqueue does not change the outbox.");

            // The connection intentionally targets a closed local port; no service is started.
            var unavailable = new SqlGameRepository("Server=tcp:127.0.0.1,1;Database=NebulaInvaders;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1;ConnectRetryCount=0;Pooling=False");
            await ExpectAsync<ArgumentOutOfRangeException>(() => unavailable.SaveAsync(sample with { Score = -1 }));
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                try
                {
                    await store.FlushAsync(unavailable, timeout.Token);
                    throw new InvalidOperationException("SQL failure test unexpectedly connected to local port 1.");
                }
                catch (SqlException) { }
                catch (OperationCanceledException) { }
            }
            Assert(await store.CountAsync() == 25, "SQL failure retains every pending game.");

            var brokenPath = Path.Combine(root, "broken.json");
            const string broken = "{broken-json-for-recovery";
            await File.WriteAllTextAsync(brokenPath, broken);
            var brokenStore = new PendingGameStore(brokenPath);
            await ExpectAsync<InvalidDataException>(() => brokenStore.CountAsync());
            await ExpectAsync<InvalidDataException>(() => brokenStore.EnqueueAsync(Sample()));
            Assert(await File.ReadAllTextAsync(brokenPath) == broken, "Corrupt JSON is never silently overwritten.");

            try
            {
                _ = new SqlGameRepository("Server=.;Database=master;Integrated Security=True");
                throw new InvalidOperationException("Repository accepted an unrelated database.");
            }
            catch (ArgumentException) { }

            Console.WriteLine("PASS Data: validation, idempotent outbox, concurrent writes, reload, cancellation, SQL failure, corrupt-file preservation.");

            var integrationConnection = Environment.GetEnvironmentVariable("NEBULA_TEST_SQL_CONNECTION");
            if (!string.IsNullOrWhiteSpace(integrationConnection))
                await RunIntegrationAsync(integrationConnection, Path.Combine(root, "integration.json"));
            else
                Console.WriteLine("SKIP SQL integration: set NEBULA_TEST_SQL_CONNECTION to a prepared test instance to enable.");
        }
        finally
        {
            // Only these known direct test files can exist in this newly generated directory.
            foreach (var filename in new[] { "pending.json", "broken.json", "integration.json" })
            {
                var file = Path.Combine(root, filename);
                if (File.Exists(file)) File.Delete(file);
            }
            Directory.Delete(root, recursive: false);
        }
    }

    private static async Task RunIntegrationAsync(string connectionString, string outboxPath)
    {
        var repository = new SqlGameRepository(connectionString);
        await repository.InitializeAsync();
        await repository.InitializeAsync(); // Repetir la preparación automática conserva las partidas.
        await repository.TestConnectionAsync();
        var result = Sample() with
        {
            PlayerName = "CHECK-" + Guid.NewGuid().ToString("N")[..16],
            Score = int.MaxValue,
            LevelReached = 5,
            Completed = true
        };
        try
        {
            // Concurrent retries must still create just one row.
            await Task.WhenAll(repository.SaveAsync(result), repository.SaveAsync(result));
            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Games WHERE Id = @Id AND PlayerName = @PlayerName AND Score = @Score AND Completed = 1;", connection);
                command.Parameters.Add("@Id", System.Data.SqlDbType.UniqueIdentifier).Value = result.Id;
                command.Parameters.Add("@PlayerName", System.Data.SqlDbType.NVarChar, 32).Value = result.PlayerName;
                command.Parameters.Add("@Score", System.Data.SqlDbType.Int).Value = result.Score;
                Assert(Convert.ToInt32(await command.ExecuteScalarAsync()) == 1, "SQL insert and concurrent idempotence.");
            }

            var store = new PendingGameStore(outboxPath);
            await store.EnqueueAsync(result);
            Assert(await store.FlushAsync(repository) == 1, "Successful SQL flush acknowledges one queued game.");
            Assert(await store.CountAsync() == 0, "Successful SQL flush clears its queued game.");
            var leaderboard = await repository.GetLeaderboardAsync(100);
            Assert(leaderboard.Any(entry => entry.PlayerName == result.PlayerName && entry.Completed), "Leaderboard reads saved data.");
            Assert(leaderboard.All(entry => entry.EndedUtc.Kind == DateTimeKind.Utc), "SQL timestamps are restored as UTC.");
            Console.WriteLine("PASS SQL integration: insert, concurrent idempotence, successful outbox flush, leaderboard.");
        }
        finally
        {
            // Opt-in integration tests delete only their newly generated, uniquely named row.
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand("DELETE FROM dbo.Games WHERE Id = @Id AND PlayerName = @PlayerName;", connection);
            cleanup.Parameters.Add("@Id", System.Data.SqlDbType.UniqueIdentifier).Value = result.Id;
            cleanup.Parameters.Add("@PlayerName", System.Data.SqlDbType.NVarChar, 32).Value = result.PlayerName;
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static GameResult Sample()
    {
        var started = DateTime.UtcNow.AddSeconds(-30);
        return new GameResult(Guid.NewGuid(), "Piloto", 120, 2, false, 30, "ESP32-WROOM-32U", started, started.AddSeconds(30));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Data check failed: " + message);
    }

    private static async Task ExpectAsync<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
