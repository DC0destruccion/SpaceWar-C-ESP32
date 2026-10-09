# Empezar con Nebula Invaders

Puedes probar primero el juego con teclado y después conectar el ESP32-WROOM-32U y SQL Server Express.

## 1. Abrir el juego

**Si usas `NebulaInvaders-ESP32-32U-Portatil.zip`:** extráelo completo y abre **`portatil/NebulaInvaders.exe`**, o `jugar.cmd`. **No se instala el juego, .NET ni Visual Studio:** el runtime ya viene en esa carpeta. Conserva todas las DLL y `appsettings.json` junto al EXE; no abras el ejecutable desde dentro del ZIP ni copies solo el EXE. Esta entrega es para Windows x64.

**Si usas solo `NebulaInvaders-ESP32-32U-Codigo.zip` para modificar o compilar:** necesitas **.NET SDK 10**; abre una terminal en la carpeta de `NebulaInvaders.sln` y ejecuta:

```powershell
dotnet run --project src/NebulaInvaders.Desktop -c Release
```

También puedes abrir `jugar.cmd`, o abrir la solución con Visual Studio compatible con .NET 10 y ejecutar el proyecto `NebulaInvaders.Desktop`. La primera compilación descarga dependencias de NuGet.

## 2. Probar una misión sin placa

1. Deja **CONTROL DE VUELO** en **Prueba · Teclado ← →**, que es la opción inicial.
2. Escribe un nombre en **PILOTO**.
3. Pulsa **INICIAR MISIÓN** y espera la breve introducción.
4. Mueve la nave con **← / →** o **A / D**. Dispara automáticamente.

**P** pausa/reanuda. **M** activa/desactiva la música durante la misión; también puedes usar **Pausa / REANUDAR** y **Música chiptune** en la ventana. Si cambias a otra aplicación, la misión se pausa. Hay **3 vidas**, protección de **1.6 s** tras recibir daño y **5 niveles** cada vez más rápidos.

## 3. Conectar el potenciómetro al ESP32-WROOM-32U

Desconecta la alimentación para cablear un potenciómetro lineal B10K:

| Terminal del potenciómetro | Pin de la placa |
|---|---|
| Un extremo | **3V3** |
| Centro, cursor | **GPIO34 / IO34** |
| Otro extremo | **GND** |

**Nunca conectes GPIO34 ni el potenciómetro a 5 V.** Usa el pin marcado **34** o **IO34**, no el contacto número 34 contando desde una esquina. El [diagrama y la guía de hardware](HARDWARE.md) muestran el cableado de esta placa de 38 pines y los pasos de carga por CP2102.

1. En Arduino IDE instala **esp32 by Espressif Systems** y selecciona **ESP32 Dev Module** para el módulo ESP32-WROOM-32U.
2. Abre `firmware/Esp32Wroom32UPotController/Esp32Wroom32UPotController.ino` desde la raíz del proyecto.
3. Usa Flash **4 MB**, PSRAM **Disabled**, velocidad de subida **115200** y la partición predeterminada de 4 MB. Conecta el micro USB de datos y selecciona su puerto COM. Esta placa usa CP2102 y no necesita una opción USB CDC.
4. Verifica y sube el sketch. En el Monitor Serie a **115200** deben aparecer lecturas como `POT:2048` al girar.
5. **Cierra el Monitor Serie**. El juego necesita usar ese puerto COM.

En el juego, selecciona **ESP32-32U · Potenciómetro**, pulsa **↻**, elige el COM y pulsa **Conectar ESP32**. Espera **Señal activa**. Gira a ambos extremos y ajusta **ADC MÍN.** y **ADC MÁX.** usando las lecturas reales: deben estar entre 0 y 4095 y separados por al menos **100**. Marca **Invertir giro** si lo necesitas.

Pulsa **INICIAR MISIÓN**: solo tienes que girar para mover y apuntar. Si se pierde la señal, se pausa; reconecta y pulsa **REANUDAR**. No se necesitan botones de juego conectados al ESP32.

## 4. Activar el registro en SQL Server Express

1. Comprueba que el **motor SQL Server Express** está instalado y en ejecución. Instalar SSMS por sí solo no instala el motor.
2. La conexión incluida utiliza **`.\SQLEXPRESS`** con tu cuenta de Windows. Si la instancia tiene otro nombre, cambia `SqlConnectionString` en `appsettings.json` junto al EXE y reinicia. En código fuente edita `src/NebulaInvaders.Desktop/appsettings.json` antes de compilar.
3. Pulsa **Preparar SQL**. Creará la base **NebulaInvaders** y su tabla si tienes permisos. Como alternativa, ejecuta `database/01-create-database.sql` completo desde SSMS.
4. Finaliza una misión y revisa **Ranking** o ejecuta `database/02-example-queries.sql` en SSMS.

En JSON el servidor predeterminado se escribe `"Server=.\\SQLEXPRESS;..."`. Mantén `Database=NebulaInvaders`. La variable **NEBULA_SQL_CONNECTION**, si está definida al iniciar, tiene prioridad sobre el archivo. La [guía SQL](../database/README.md) incluye la cadena completa y los permisos necesarios.

Si SQL falla, los resultados escritos correctamente quedan en **`%LOCALAPPDATA%\NebulaInvaders\pending-games.json`**. Pulsa **Preparar SQL** después de corregir la conexión; también se reintentan al terminar otra partida. Cerrar normalmente una misión guarda un resultado parcial pendiente. **No hay guardado para reanudar una partida** y un cierre forzado puede perder el progreso aún no registrado.

La versión portátil no instala ni incluye el motor SQL Server. Puedes jugar sin instalarlo y enviar tus resultados más adelante; para el guardado en SQL utiliza tu instancia Express existente o una instancia remota accesible. La cola local es temporal y no sustituye a SQL Server. El programa escribe los pendientes en la carpeta del usuario aunque se ejecute desde un USB.

## 5. Conservar ajustes y comprobar el proyecto

Los cambios de calibración, giro y música de la ventana duran esa sesión. Para usarlos por defecto en el próximo inicio, edita `PotMinimum`, `PotMaximum`, `InvertPotentiometer` y `MusicEnabled` en `appsettings.json` con el juego cerrado. `DefaultPlayerName` define el nombre inicial.

Con el código fuente y SDK 10 puedes ejecutar `comprobar.cmd` o:

```powershell
dotnet run --project tests/NebulaInvaders.Checks -c Release
```

Si no aparecen datos del ESP32, revisa cable micro USB de datos, controlador CP210x y puerto libre. Si la carga queda en Connecting, mantén BOOT, pulsa y suelta EN y suelta BOOT cuando comience la escritura; al terminar pulsa EN si no arranca. Si falla SQL, revisa nombre de instancia, servicio y permisos. Si el EXE solicita .NET, estás abriendo la entrega anterior o una carpeta incompleta: extrae de nuevo el ZIP **Portatil** y usa su EXE. Para errores de calibración deja como punto de partida mínimo **0**, máximo **4095**.

La placa física, la compilación/carga Arduino y el guardado contra una instancia SQL real **siguen pendientes de comprobar en tu equipo**. Las pruebas de consola no sustituyen esas verificaciones. Encontrarás arquitectura, velocidades y configuración completa en el [README principal](../README.md).
