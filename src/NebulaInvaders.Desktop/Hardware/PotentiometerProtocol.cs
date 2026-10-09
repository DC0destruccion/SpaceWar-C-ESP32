using System.Globalization;

namespace NebulaInvaders.Desktop.Hardware;

public static class PotentiometerProtocol
{
    /// Rechaza ruido de arranque y mensajes corruptos sin detener la lectura del puerto.
    public static bool TryParse(string line, out int value)
    {
        value = 0;
        ReadOnlySpan<char> text = line.AsSpan().Trim();
        if (!text.StartsWith("POT:", StringComparison.Ordinal)) return false;
        var digits = text[4..];
        // Validar ASCII explícitamente impide aceptar signos, cifras de otras culturas o NUL final.
        foreach (char digit in digits) if (digit is < '0' or > '9') return false;
        return digits.Length is >= 1 and <= 4 &&
            int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 4095;
    }

    
    public static float Normalize(int raw, int minimum, int maximum, bool invert)
    {
        if (minimum is < 0 or > 4095 || maximum is < 0 or > 4095 || maximum - minimum < 100)
            throw new ArgumentOutOfRangeException(nameof(maximum), "El intervalo de calibración debe ser al menos 100.");
        float value = Math.Clamp(((float)raw - minimum) / (maximum - minimum), 0f, 1f);
        return invert ? 1f - value : value;
    }
}
