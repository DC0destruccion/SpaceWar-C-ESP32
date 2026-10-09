USE [NebulaInvaders];
GO

-- Mejores 10 partidas. Cada fila corresponde a una partida, no a un jugador único.
SELECT TOP (10) PlayerName AS Jugador, Score AS Puntos, LevelReached AS Nivel,
    Completed AS Victoria, DurationSeconds AS Segundos, ControlMode AS Control,
    EndedUtc AS FinalUTC
FROM dbo.Games
ORDER BY Score DESC, LevelReached DESC, Completed DESC, EndedUtc ASC, Id ASC;
GO

-- Resumen por jugador.
SELECT PlayerName AS Jugador, COUNT_BIG(*) AS Partidas,
    MAX(Score) AS MejorPuntuacion, SUM(CONVERT(bigint, Completed)) AS Victorias,
    SUM(CONVERT(bigint, DurationSeconds)) AS SegundosJugados
FROM dbo.Games
GROUP BY PlayerName
ORDER BY MejorPuntuacion DESC;
GO

-- Historial reciente.
SELECT TOP (50) * FROM dbo.Games ORDER BY EndedUtc DESC;
GO
