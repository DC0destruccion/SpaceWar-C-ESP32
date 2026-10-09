# Conexión a la instancia del equipo de presentación

Servidor en SSMS: `(localdb)\MSSQLLocalDB`, con autenticación de Windows.
El proyecto y el ejecutable portátil usan ahora esta instancia por defecto.

1. Cierra y vuelve a abrir el juego para leer appsettings.json.
2. Pulsa **Preparar SQL** para preparar el esquema y enviar las partidas pendientes.
3. Pulsa **Ranking**.

En SSMS, actualiza Bases de datos y abre NebulaInvaders > Tablas > dbo.Games.
Para mostrar las partidas, ejecuta:

```sql
USE NebulaInvaders;
SELECT PlayerName, Score, LevelReached, Completed, EndedUtc
FROM dbo.Games
ORDER BY Score DESC, LevelReached DESC, Completed DESC, EndedUtc ASC;
```

El script completo de creación está en database/01-create-database.sql.
Si ejecutas otra copia del juego, cambia SqlConnectionString en el appsettings.json junto a ese EXE al valor del archivo de configuración de este proyecto. En JSON la barra invertida se escribe doble.
La variable de entorno NEBULA_SQL_CONNECTION, si está definida, tiene prioridad sobre el archivo.

Verificación realizada: creación de la base, inserción, reintentos sin duplicados, sincronización de cola y consulta de ranking con SQL Server LocalDB real. La partida de prueba se eliminó al terminar.

Actualización: el juego prepara SQL y sincroniza pendientes automáticamente al abrir, al terminar cada partida y antes de consultar Ranking. Ya no es necesario pulsar Preparar SQL para el funcionamiento normal.
