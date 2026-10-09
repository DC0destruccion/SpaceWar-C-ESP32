using System.Globalization;
using NebulaInvaders.Desktop.Hardware;

namespace NebulaInvaders.Checks;

/// <summary>Comprueba todos los valores ADC y mensajes inválidos sin abrir un puerto COM.</summary>
internal static class ProtocolChecks
{
    public static void Run()
    {
        (string Name, Action Test)[] tests =
        [
            ("ESP32: todo el rango ADC de 12 bits y finales de línea", AdcRangeAndLineEndings),
            ("ESP32: ruido, datos corruptos y valores fuera de rango", RejectMalformedMessages),
            ("Potenciómetro: calibración, saturación, inversión y recorrido continuo", NormalizeTravel),
            ("Potenciómetro: rechazo de calibración inválida sin desbordamientos", RejectInvalidCalibration)
        ];
        foreach (var (name, test) in tests)
        {
            test();
            Console.WriteLine($"[OK] {name}");
        }
    }

    private static void AdcRangeAndLineEndings()
    {
        for (int expected = 0; expected <= 4095; expected++)
        {
            string message = "POT:" + expected.ToString(CultureInfo.InvariantCulture);
            True(PotentiometerProtocol.TryParse(message, out int actual), $"Aceptar {message}");
            True(expected == actual, $"Conservar el valor exacto de {message}");
        }
        foreach (string message in new[] { "POT:2048\r", "POT:2048\n", "POT:2048\r\n" })
            True(PotentiometerProtocol.TryParse(message, out int actual) && actual == 2048,
                "Aceptar finales LF/CRLF sin alterar la lectura");
    }

    private static void RejectMalformedMessages()
    {
        string[] invalid =
        [
            "", "\r\n", "ESP-ROM:esp32s3-20210327", "rst:0x1 (POWERON)",
            "2048", "POT", "POT:", "POT :2048", "pot:2048", "noisePOT:2048",
            "POT:-1", "POT:+1", "POT:4096", "POT:9999", "POT:65535",
            "POT:2147483647", "POT:9999999999999999999999999999", "POT:1.0",
            "POT:1,000", "POT:1e2", "POT: 1", "POT:2 048", "POT:20\t48",
            "POT:1\0", "POT:12\0", "POT:1\u0001", "POT:１２", "POT:١٢",
            "POT:10POT:20", "POT:10\nPOT:20", "POT:20;OK"
        ];
        foreach (string message in invalid)
            True(!PotentiometerProtocol.TryParse(message, out _),
                $"Descartar mensaje corrupto: {Escape(message)}");
        True(!PotentiometerProtocol.TryParse(null!, out _), "Descartar entrada nula sin excepción");
    }

    private static void NormalizeTravel()
    {
        Near(0, PotentiometerProtocol.Normalize(0, 0, 4095, false), "Extremo izquierdo");
        Near(1, PotentiometerProtocol.Normalize(4095, 0, 4095, false), "Extremo derecho");
        Near(0.5f, PotentiometerProtocol.Normalize(2000, 200, 3800, false), "Centro calibrado");
        Near(0, PotentiometerProtocol.Normalize(0, 200, 3800, false), "Saturación inferior");
        Near(1, PotentiometerProtocol.Normalize(4095, 200, 3800, false), "Saturación superior");
        Near(0, PotentiometerProtocol.Normalize(int.MinValue, 200, 3800, false), "Saturación sin desbordamiento inferior");
        Near(1, PotentiometerProtocol.Normalize(int.MaxValue, 200, 3800, false), "Saturación sin desbordamiento superior");
        Near(0.5f, PotentiometerProtocol.Normalize(50, 0, 100, false), "Intervalo mínimo permitido");
        Near(0.5f, PotentiometerProtocol.Normalize(4045, 3995, 4095, false), "Calibración junto al máximo ADC");

        float previous = -1f;
        for (int raw = 0; raw <= 4095; raw++)
        {
            float normal = PotentiometerProtocol.Normalize(raw, 175, 3860, false);
            float inverted = PotentiometerProtocol.Normalize(raw, 175, 3860, true);
            True(float.IsFinite(normal) && normal is >= 0f and <= 1f, "Posición normal acotada");
            True(float.IsFinite(inverted) && inverted is >= 0f and <= 1f, "Posición invertida acotada");
            True(normal >= previous, "Girar el potenciómetro no invierte el avance");
            Near(1f, normal + inverted, "Invertir refleja el recorrido completo");
            previous = normal;
        }
    }

    private static void RejectInvalidCalibration()
    {
        (int Minimum, int Maximum)[] invalid =
        [
            (-1, 4095), (0, 4096), (0, -1), (4096, 4095),
            (1000, 1000), (3000, 1000), (1000, 1099),
            (1, int.MinValue), (int.MaxValue, -100), (int.MinValue, int.MaxValue)
        ];
        foreach (var (minimum, maximum) in invalid)
        {
            bool rejected = false;
            try { PotentiometerProtocol.Normalize(2048, minimum, maximum, false); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            True(rejected, $"Rechazar intervalo inválido [{minimum}, {maximum}]");
        }
    }

    private static string Escape(string text) => text
        .Replace("\0", "\\0").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Near(float expected, float actual, string message)
        => True(Math.Abs(expected - actual) <= 0.00001f,
            $"{message}: esperado {expected}, obtenido {actual}");
}
