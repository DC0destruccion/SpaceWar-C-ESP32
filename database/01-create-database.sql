USE [master];
GO
IF DB_ID(N'NebulaInvaders') IS NULL
    CREATE DATABASE [NebulaInvaders];
GO
USE [NebulaInvaders];
GO
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
        -- UUID generado por C#: permite reintentar una inserción sin duplicar la partida.
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Games PRIMARY KEY,
        PlayerName nvarchar(32) NOT NULL,
        Score int NOT NULL CONSTRAINT CK_Games_Score CHECK (Score >= 0),
        LevelReached tinyint NOT NULL CONSTRAINT CK_Games_Level CHECK (LevelReached BETWEEN 1 AND 5),
        Completed bit NOT NULL,
        DurationSeconds int NOT NULL CONSTRAINT CK_Games_Duration CHECK (DurationSeconds >= 0),
        ControlMode nvarchar(24) NOT NULL,
        -- UTC evita depender de la zona horaria del PC. La interfaz convierte al mostrar.
        StartedUtc datetime2(7) NOT NULL,
        EndedUtc datetime2(7) NOT NULL,
        SavedUtc datetime2(7) NOT NULL CONSTRAINT DF_Games_SavedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Games_PlayerName CHECK (LEN(LTRIM(RTRIM(PlayerName))) BETWEEN 1 AND 32),
        CONSTRAINT CK_Games_ControlMode CHECK (LEN(LTRIM(RTRIM(ControlMode))) BETWEEN 1 AND 24),
        CONSTRAINT CK_Games_Completed CHECK (Completed = 0 OR LevelReached = 5),
        CONSTRAINT CK_Games_Dates CHECK (EndedUtc >= StartedUtc)
    );
END;

-- Este índice sigue el orden del ranking 
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Games_Leaderboard' AND object_id = OBJECT_ID(N'dbo.Games'))
    CREATE INDEX IX_Games_Leaderboard
        ON dbo.Games (Score DESC, LevelReached DESC, Completed DESC, EndedUtc ASC)
        INCLUDE (PlayerName);
COMMIT TRANSACTION;
GO
