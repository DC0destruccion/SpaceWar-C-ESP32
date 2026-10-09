# Verificación de la entrega

Fecha: 6 de octubre de 2026.

Adaptación actual: ESP32-WROOM-32U / DevKitC de 38 pines con CP2102. El firmware usa GPIO34 y el cliente C# deja DTR y RTS desactivados durante el juego. El formato serie y la estructura de SQL se conservan.

## Comprobado en este entorno

- Solución C# compilada en Release con .NET SDK 10.0.401, sin errores ni advertencias.
- Publicación portátil Windows x64, autocontenida, con .NET y Windows Forms 10.0.12 incluidos. No requiere instalar .NET para jugar.
- Arranque verificado con `DOTNET_ROOT` y `DOTNET_ROOT_X64` apuntando a una carpeta vacía. El registro del host confirmó la carga de `coreclr.dll` desde `portatil/`; no se desinstaló el .NET del equipo de desarrollo.
- Comentarios en español añadidos al motor, interfaz, protocolo, lector serie, música, configuración, persistencia, modelos, pruebas y firmware. La guía `LEER_EL_CODIGO.md` explica el orden de lectura.
- Once grupos del motor: dificultad creciente; introducción; movimiento y límites; disparo automático; colisiones y partículas; escudo y tres vidas; derrota por invasión; transición por los cinco niveles y victoria; reinicio; simulación determinista; pausa y fotogramas lentos.
- Cuatro grupos del protocolo: los 4096 valores ADC; finales de línea; rechazo de ruido y mensajes corruptos; calibración, inversión, saturación y límites inválidos sin desbordamiento.
- Persistencia local: validación, duplicados, escrituras concurrentes, reapertura, cancelación, conservación ante fallo de conexión SQL y conservación de archivos dañados.
- Comprobación de interfaz: inicio, bloqueo de controles durante la misión, pausa/reanudación, pausa por señal ESP32 ausente y bloqueo de reanudación sin señal.
- Síntesis WAV: cabecera y longitud válidas; carga mediante SoundPlayer. No se verificó auditivamente la salida de un altavoz.
- Pantallas de inicio, partida y pausa renderizadas desde los controles reales de Windows Forms e inspeccionadas visualmente. Las imágenes están en `docs/preview/`.

## Pendiente en tu equipo

- **ESP32-WROOM-32U real:** compilar y cargar el sketch usando la configuración exacta de tu placa, comprobar el COM y calibrar el potenciómetro. No se dispuso de placa ni compilador Arduino durante la entrega.
- **SQL Server Express activo:** ejecutar Preparar SQL, terminar una partida y comprobar `dbo.Games` y Ranking. Se verificó el tratamiento de fallo de conexión, pero no se ejecutaron inserciones ni consultas contra un servidor activo.
- **Altavoces:** escuchar la música y comprobar el volumen del mezclador de Windows.

## Repetir las comprobaciones

Desde la carpeta del proyecto, con .NET SDK 10:

```powershell
dotnet build NebulaInvaders.sln -c Release
dotnet run --project tests/NebulaInvaders.Checks -c Release
dotnet run --project src/NebulaInvaders.Desktop -c Release -- --render-preview docs/preview
```

El último comando abre brevemente una ventana invisible para crear los controles, ejecuta las comprobaciones de interfaz, genera tres PNG y carga el WAV sin reproducirlo. No crea partidas en SQL ni modifica la cola personal. Las comprobaciones de disco usan una carpeta temporal propia.

La comprobación de SQL real es optativa mediante `NEBULA_TEST_SQL_CONNECTION`, descrita en [la guía de base de datos](../database/README.md). Sin esa variable se indica `SKIP SQL integration`; eso no significa que SQL se haya probado.
