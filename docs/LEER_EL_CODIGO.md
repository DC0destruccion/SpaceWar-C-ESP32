# Guía del código comentado

Los comentarios están dentro de los archivos C#, el firmware y el script SQL. Explican responsabilidades, unidades y decisiones del algoritmo; no es necesario consultar esta guía mientras lees cada método.

## Orden recomendado

1. **[Program.cs](../src/NebulaInvaders.Desktop/Program.cs):** inicio, configuración, bloqueo de una segunda instancia y bucle de Windows Forms.
2. **[Models.cs](../src/NebulaInvaders.Core/Models.cs):** estados de la misión, enemigos, balas, partículas y tabla de dificultad de los cinco niveles.
3. **[GameEngine.cs](../src/NebulaInvaders.Core/GameEngine.cs):** movimiento, disparos automáticos, colisiones, vidas y cambio de nivel. `Update` recibe tiempo en segundos y una posición de 0 a 1; divide el tiempo en pasos pequeños para detectar impactos.
4. **[MainForm.cs](../src/NebulaInvaders.Desktop/MainForm.cs):** conecta botones y temporizador con el motor. `TickGame` obtiene entrada, avanza la partida y solicita el dibujo. `SaveResultAsync` conserva el resultado; `ClosingAsync` espera antes de cerrar.
5. **[GameCanvas.cs](../src/NebulaInvaders.Desktop/GameCanvas.cs):** dibujo y animaciones. El escenario lógico no cambia al redimensionar la ventana. Sus métodos dibujan; las colisiones se resuelven en el motor.
6. **[PotentiometerProtocol.cs](../src/NebulaInvaders.Desktop/Hardware/PotentiometerProtocol.cs)** y **[PotentiometerController.cs](../src/NebulaInvaders.Desktop/Hardware/PotentiometerController.cs):** validación de mensajes, calibración y lectura USB en segundo plano.
7. **[Firmware del ESP32-WROOM-32U](../firmware/Esp32Wroom32UPotController/Esp32Wroom32UPotController.ino):** promedio de lecturas, suavizado y envío de `POT:valor` cada 20 ms aproximadamente.
8. **[SynthMusic.cs](../src/NebulaInvaders.Desktop/Audio/SynthMusic.cs):** convierte notas en ondas, mezcla tres voces y escribe una cabecera WAV en memoria.
9. **[SqlGameRepository.cs](../src/NebulaInvaders.Data/SqlGameRepository.cs)** y **[PendingGameStore.cs](../src/NebulaInvaders.Data/PendingGameStore.cs):** parámetros SQL, transacciones, duplicados y escritura segura de pendientes.
10. **[GameResult.cs](../src/NebulaInvaders.Data/GameResult.cs)** y **[script SQL](../database/01-create-database.sql):** datos que se guardan y reglas para validarlos.

## Ejemplo: del potenciómetro al movimiento

El ESP32 envía `POT:2048`. El receptor verifica que sea un entero entre 0 y 4095. Con calibración 0–4095, `Normalize` devuelve aproximadamente 0.5. El motor calcula una posición objetivo cercana al centro y acerca gradualmente la nave para evitar vibración. El dibujante muestra la nueva posición sin conocer el puerto COM.

## Ejemplo: guardar una puntuación

Al terminar se crea un `GameResult` con un UUID. Primero se escribe en la cola JSON del usuario. Después se intenta insertar en SQL usando parámetros. Si SQL no responde, permanece en la cola. Al reintentar se utiliza el mismo UUID y la transacción evita otra copia de esa partida.

## Cambios habituales

- **Dificultad:** valores de `LevelCatalog` en `Models.cs`.
- **Disparo automático:** `AutoFireInterval` en `GameEngine.cs`, en segundos.
- **Aspecto:** colores en `Theme.cs` y matrices de píxeles `Sprites` en `GameCanvas.cs`.
- **Música:** notas MIDI y duración `step` en `SynthMusic.cs`.
- **Calibración y SQL:** `appsettings.json`; no admite comentarios porque debe seguir siendo JSON válido.
- **Crear la versión sin instalación:** `publicar.ps1` o el perfil de publicación `Portatil`. Compilar necesita SDK; jugar con la carpeta publicada no.

Las comprobaciones de `tests/NebulaInvaders.Checks` prueban casos de funcionamiento y error. La comprobación de SQL real se activa solo mediante `NEBULA_TEST_SQL_CONNECTION` en una instancia de pruebas preparada.
