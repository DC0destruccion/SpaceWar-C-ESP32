using System.Data;
using Microsoft.Data.SqlClient;

namespace NebulaInvaders.Data;

/// Acceso a SQL Server mediante conexiones cortas y consultas parametrizadas.
/// La interfaz prepara automáticamente el esquema antes de sincronizar resultados.

public sealed class SqlGameRepository
{
    public const string DatabaseName = "NebulaInvaders";
    public const string DefaultConnectionString = @"Server=(localdb)\MSSQLLocalDB;Database=NebulaInvaders;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5";
    private readonly string _connectionString;

    public SqlGameRepository(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, DatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Usa Database={DatabaseName} en la conexión.", nameof(connectionString));
        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename))
            throw new ArgumentException("La conexión debe usar una base de datos del servidor; no se admite AttachDbFilename.", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new ArgumentException("Falta el servidor en la conexión.", nameof(connectionString));
        _connectionString = builder.ConnectionString;
    }

    /// Conecta a master
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var masterBuilder = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var create = new SqlCommand("IF DB_ID(N'NebulaInvaders') IS NULL CREATE DATABASE [NebulaInvaders];", master) { CommandTimeout = 30 };
            try
            {
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == 1801)
            {
                
            }
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var schema = new SqlCommand(SchemaSql, connection) { CommandTimeout = 30 };
        await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Comprueba tanto la conexión como la existencia de las columnas esperadas.</summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand("SELECT TOP (0) Id, PlayerName, Score, LevelReached, Completed, DurationSeconds, ControlMode, StartedUtc, EndedUtc FROM dbo.Games;", connection) { CommandTimeout = 5 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// Inserta una sola vez por UID
    public async Task SaveAsync(GameResult result, CancellationToken cancellationToken = default)
    {
        GameResultValidator.Validate(result);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // La comprobación y el INSERT comparten transacción. UPDLOCK + HOLDLOCK evitan
        // que dos solicitudes simultáneas decidan insertar el mismo identificador.
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF NOT EXISTS (SELECT 1 FROM dbo.Games WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id)
                INSERT INTO dbo.Games
                    (Id, PlayerName, Score, LevelReached, Completed, DurationSeconds, ControlMode, StartedUtc, EndedUtc)
                VALUES
                    (@Id, @PlayerName, @Score, @LevelReached, @Completed, @DurationSeconds, @ControlMode, @StartedUtc, @EndedUtc);
            COMMIT TRANSACTION;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        // Los parámetros separan datos de instrucciones SQL: un nombre con comillas
        // se guarda como texto y nunca se concatena al comando. Los tipos evitan conversiones ambiguas.
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = result.Id;
        command.Parameters.Add("@PlayerName", SqlDbType.NVarChar, 32).Value = result.PlayerName;
        command.Parameters.Add("@Score", SqlDbType.Int).Value = result.Score;
        command.Parameters.Add("@LevelReached", SqlDbType.TinyInt).Value = result.LevelReached;
        command.Parameters.Add("@Completed", SqlDbType.Bit).Value = result.Completed;
        command.Parameters.Add("@DurationSeconds", SqlDbType.Int).Value = result.DurationSeconds;
        command.Parameters.Add("@ControlMode", SqlDbType.NVarChar, 24).Value = result.ControlMode;
        command.Parameters.Add("@StartedUtc", SqlDbType.DateTime2).Value = result.StartedUtc;
        command.Parameters.Add("@EndedUtc", SqlDbType.DateTime2).Value = result.EndedUtc;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Obtiene las mejores partidas; el orden secundario resuelve empates de forma estable.</summary>
    public async Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "El límite debe estar entre 1 y 100.");
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            SELECT TOP (@Limit) PlayerName, Score, LevelReached, Completed, EndedUtc
            FROM dbo.Games
            ORDER BY Score DESC, LevelReached DESC, Completed DESC, EndedUtc ASC, Id ASC;
            """;
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 10 };
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<LeaderboardEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new LeaderboardEntry(
                reader.GetString(0), reader.GetInt32(1), reader.GetByte(2), reader.GetBoolean(3),
                DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)));
        }
        return entries;
    }

    // El bloqueo de aplicación serializa la preparación del esquema. Las restricciones
    // CHECK también protegen los datos si alguien escribe directamente desde SSMS.
    private const string SchemaSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @LockResult int;
        EXEC @LockResult = sys.sp_getapplock
            @Resource = N'NebulaInvaders.Schema.v1', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
        IF @LockResult < 0 THROW 50001, N'No se pudo bloquear la preparación del esquema.', 1;
        IF OBJECT_ID(N'dbo.Games', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Games
            (
                Id uniqueidentifier NOT NULL CONSTRAINT PK_Games PRIMARY KEY,
                PlayerName nvarchar(32) NOT NULL,
                Score int NOT NULL CONSTRAINT CK_Games_Score CHECK (Score >= 0),
                LevelReached tinyint NOT NULL CONSTRAINT CK_Games_Level CHECK (LevelReached BETWEEN 1 AND 5),
                Completed bit NOT NULL,
                DurationSeconds int NOT NULL CONSTRAINT CK_Games_Duration CHECK (DurationSeconds >= 0),
                ControlMode nvarchar(24) NOT NULL,
                StartedUtc datetime2(7) NOT NULL,
                EndedUtc datetime2(7) NOT NULL,
                SavedUtc datetime2(7) NOT NULL CONSTRAINT DF_Games_SavedUtc DEFAULT SYSUTCDATETIME(),
                CONSTRAINT CK_Games_PlayerName CHECK (LEN(LTRIM(RTRIM(PlayerName))) BETWEEN 1 AND 32),
                CONSTRAINT CK_Games_ControlMode CHECK (LEN(LTRIM(RTRIM(ControlMode))) BETWEEN 1 AND 24),
                CONSTRAINT CK_Games_Completed CHECK (Completed = 0 OR LevelReached = 5),
                CONSTRAINT CK_Games_Dates CHECK (EndedUtc >= StartedUtc)
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Games_Leaderboard' AND object_id = OBJECT_ID(N'dbo.Games'))
            CREATE INDEX IX_Games_Leaderboard ON dbo.Games (Score DESC, LevelReached DESC, Completed DESC, EndedUtc ASC)
                INCLUDE (PlayerName);
        COMMIT TRANSACTION;
        """;
}
