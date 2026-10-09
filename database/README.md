# SQL Server Express

El juego utiliza SQL Server real mediante `Microsoft.Data.SqlClient`. La conexión local predeterminada es:

```text
Server=.\SQLEXPRESS;Database=NebulaInvaders;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5
```

`SQLEXPRESS` es el nombre habitual de una instancia Express, pero se puede elegir otro al instalar. Comprueba el nombre en SQL Server Configuration Manager o al conectarte con SQL Server Management Studio (SSMS). Si tu instancia tiene otro nombre, cambia solo `Server` en la configuración del juego. Para una instancia predeterminada instalada en el mismo PC, suele ser `Server=.`. La aplicación exige `Database=NebulaInvaders` y no acepta adjuntar archivos MDF.

## Preparar la base

1. Asegúrate de tener instalado el **motor SQL Server Express**, además de SSMS si deseas consultar los datos. SSMS por sí solo no instala el motor.
2. Comprueba que tu instancia está en ejecución. La aplicación no instala SQL Server ni inicia sus servicios.
3. Configura la conexión del juego y pulsa **Preparar SQL**. Es una acción explícita: crea la base `NebulaInvaders` si falta y crea `dbo.Games` y su índice si no existen. No borra partidas existentes.
4. También puedes abrir `01-create-database.sql` en SSMS, conectarte a tu instancia y ejecutarlo completo. Los separadores `GO` son para SSMS/sqlcmd y no se envían como comandos ADO.NET.
5. Comprueba que aparezca **SQL Server conectado**, juega una partida y abre **Ranking**. También puedes consultar los datos con `02-example-queries.sql`.

La preparación necesita permisos para crear la base y las tablas. Si tu usuario no los tiene, un administrador puede ejecutar el script y conceder a tu usuario acceso a la base y permisos `SELECT` e `INSERT` sobre `dbo.Games`; el funcionamiento cotidiano no necesita borrar ni actualizar registros. No se incorporan contraseñas al código.

`Integrated Security=True` usa la cuenta de Windows del usuario que ejecuta el juego. `TrustServerCertificate=True` permite el certificado local autofirmado típico de una instalación educativa. Si utilizas un servidor remoto con certificado confiable, configura `TrustServerCertificate=False` y la identidad/servidor correspondientes.

## Qué se guarda

| Columna | Significado |
| --- | --- |
| `Id` | UUID único de la partida; evita duplicados al reintentar. |
| `PlayerName` | Nombre de 1 a 32 caracteres. |
| `Score` | Puntuación total. |
| `LevelReached` | Último nivel alcanzado, del 1 al 5. |
| `Completed` | `1` si se completaron los cinco niveles. |
| `DurationSeconds` | Duración de juego registrada en segundos. |
| `ControlMode` | Control utilizado; por ejemplo, ESP32-WROOM-32U o teclado. |
| `StartedUtc`, `EndedUtc` | Inicio y final en UTC, independiente de la zona horaria. |
| `SavedUtc` | Momento UTC en que SQL confirmó el primer guardado. |

Todas las consultas del juego usan parámetros con tipos explícitos. La clasificación ordena primero por puntuación, después por nivel y victoria, y finalmente por fecha para resolver empates.

## Si SQL no está disponible

Cada resultado pasa primero por una cola JSON local de partidas pendientes. La aplicación muestra cuándo quedan partidas por sincronizar. La cola se escribe mediante un archivo temporal y una sustitución en la misma carpeta para evitar un JSON truncado. Cada registro se elimina de la cola **solo después** de que SQL confirme su inserción. Si la conexión se corta tras insertar pero antes de confirmar, el siguiente intento usa el mismo UUID y SQL no duplica la partida.

La aplicación debe ejecutarse una sola vez por usuario para compartir esa cola de forma segura. Dentro del proceso, la biblioteca serializa todas las lecturas y escrituras, incluso si se crean varias instancias del almacén para la misma ruta. El código de la interfaz decide la ruta dentro de `%LOCALAPPDATA%\NebulaInvaders`; no guardes allí contraseñas de SQL.

Si el JSON está dañado, la biblioteca devuelve un error y conserva el archivo intacto; no lo sustituye por una lista vacía. Haz una copia antes de repararlo. Borrar o modificar manualmente ese archivo puede perder resultados pendientes.

## Validación incluida

Las comprobaciones automáticas verifican validación de datos, duplicados, escritura simultánea, persistencia tras reabrir el archivo, cancelación, conservación de JSON dañado y retención de resultados cuando SQL no responde. No inician servicios ni requieren un servidor instalado. La inserción y consulta contra una instancia real deben comprobarse en tu SQL Server Express después de prepararlo.

Para habilitar la comprobación de integración, utiliza una instancia de pruebas con la base ya preparada y establece la variable de entorno `NEBULA_TEST_SQL_CONNECTION` antes de ejecutar el proyecto `tests/NebulaInvaders.Checks`. Por ejemplo, en PowerShell:

```powershell
$env:NEBULA_TEST_SQL_CONNECTION = 'Server=.\SQLEXPRESS;Database=NebulaInvaders;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet run --project tests/NebulaInvaders.Checks
Remove-Item Env:NEBULA_TEST_SQL_CONNECTION
```

Esta comprobación agrega una partida temporal `CHECK-...`, prueba reintentos simultáneos, sincronización y clasificación, y elimina únicamente su propia fila mediante UUID y nombre. Necesita también permiso `DELETE` para esa limpieza. No crea ni elimina la base. Si SQL falla durante la limpieza, puede quedar una fila `CHECK-...` que deberá eliminarse manualmente en la instancia de pruebas. Sin esa variable, la integración se marca como omitida y no se afirma que se haya probado un guardado real.

Documentación oficial: [instancias con nombre de SQL Server](https://learn.microsoft.com/en-us/sql/sql-server/connect-to-database-engine?view=sql-server-ver17), [cadenas de conexión de SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/connection-string-syntax?view=sql-server-ver17), [parámetros de SqlCommand](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlcommand.parameters).
