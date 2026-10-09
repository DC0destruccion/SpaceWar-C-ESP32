/*
 * Nebula Invaders — mando con potenciómetro para ESP32-WROOM-32U.
 * Placa: ESP32 clásico de 38 pines, puente CP2102 y conector micro-USB.
 * Arduino IDE: "ESP32 Dev Module", Flash 4 MB, PSRAM Disabled.
 *
 * Extremos del potenciómetro B10K: 3V3 y GND. Cursor central: GPIO34.
 * NO conectes el potenciómetro ni GPIO34 a 5 V, VIN o VBUS.
 * El número 34 es el GPIO rotulado IO34/34, no la posición física del pin.
 *
 * Serial usa UART0, que el CP2102 convierte a un puerto COM del PC.
 * Esta placa no utiliza "USB CDC On Boot" ni USB nativo del ESP32-S3.
 * No se inicia Wi-Fi ni Bluetooth. Solo se necesita el cable USB de datos.
 * Consulta ../../docs/HARDWARE.md para cableado y carga paso a paso.
 * Protocolo compatible con C#: POT:<0..4095> seguido de LF, unos 50 Hz.
 */

#include <Arduino.h>

// Impide cargar por accidente una compilación para S3, S2, C3 u otra familia.
#if !defined(CONFIG_IDF_TARGET_ESP32)
#error "Selecciona ESP32 Dev Module (ESP32 clasico) en Herramientas > Placa."
#endif

constexpr uint8_t POT_PIN = 34;         // GPIO34 = ADC1_CH6; solo entrada, sin pull-up.
constexpr uint8_t SAMPLE_COUNT = 8;     // Lecturas ADC que forman cada promedio.
constexpr uint32_t SERIAL_BAUD = 115200;
constexpr uint32_t SAMPLE_INTERVAL_MS = 20; // 1000 / 20 = 50 muestras por segundo.
constexpr float SMOOTHING = 0.35f;      // Mayor valor = respuesta más rápida.

float filteredValue = 0.0f;
uint32_t lastSampleMs = 0;

// Promediar varias lecturas reduce ruido eléctrico antes del suavizado temporal.
float readPotAverage() {
  uint32_t total = 0;
  for (uint8_t index = 0; index < SAMPLE_COUNT; ++index) {
    total += analogRead(POT_PIN);
    delayMicroseconds(100);
  }
  return static_cast<float>(total) / SAMPLE_COUNT;
}

// Arduino ejecuta setup una sola vez al encender o reiniciar la placa.
void setup() {
  // UART0 ya está cableada al CP2102; no hace falta unir cables a TX/RX.
  Serial.begin(SERIAL_BAUD);
  // No esperamos a que se abra el COM: el mando comienza a emitir al arrancar.
  pinMode(POT_PIN, INPUT);
  analogReadResolution(12);             // Lectura cruda entre 0 y 4095.
  analogSetPinAttenuation(POT_PIN, ADC_11db);

  // Inicializar desde la posición real evita un salto artificial desde cero.
  filteredValue = readPotAverage();
  lastSampleMs = millis();
}

// loop se repite continuamente. El control de tiempo limita el envío a unos 50 Hz.
void loop() {
  const uint32_t now = millis();
  // La resta sin signo sigue funcionando cuando millis() se desborda.
  if (static_cast<uint32_t>(now - lastSampleMs) < SAMPLE_INTERVAL_MS) {
    delay(1);
    return;
  }
  lastSampleMs = now;

  const float average = readPotAverage();
  // Filtro exponencial: conserva parte del valor previo y se acerca al promedio nuevo.
  // +0.5 redondea al entero más cercano; constrain conserva el contrato de C#.
  filteredValue += SMOOTHING * (average - filteredValue);
  const int value = constrain(static_cast<int>(filteredValue + 0.5f), 0, 4095);

  char line[16];
  // LF delimita cada mensaje: USB puede entregar una línea en varios fragmentos.
  const int length = snprintf(line, sizeof(line), "POT:%d\n", value);
  // Si el buffer UART está lleno, se omite la muestra para no bloquear el muestreo.
  // No hay confirmación de recepción: la UART sigue transmitiendo aunque el COM esté cerrado.
  if (length > 0 && length < static_cast<int>(sizeof(line)) && Serial.availableForWrite() >= length) {
    Serial.write(reinterpret_cast<const uint8_t *>(line), length);
  }
}
