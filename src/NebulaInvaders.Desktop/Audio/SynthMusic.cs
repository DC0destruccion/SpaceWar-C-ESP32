using System.Media;
using System.Text;

namespace NebulaInvaders.Desktop.Audio;

/// <summary>Un bucle chiptune original, sintetizado en memoria. No necesita archivos de música.</summary>
public sealed class SynthMusic : IDisposable
{
    private readonly MemoryStream _wave;
    private readonly SoundPlayer _player;
    public SynthMusic()
    {
        _wave = new MemoryStream(CreateWave());
        _player = new SoundPlayer(_wave);
        _player.Load();
    }
    /// <summary>SoundPlayer repite el WAV completo; apagar la música no afecta al reloj del juego.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled) _player.PlayLooping();
        else _player.Stop();
    }
    /// <summary>Construye un WAV PCM mono de 16 bits con melodía, bajo y percusión mezclados.</summary>
    internal static byte[] CreateWave()
    {
        const int rate = 22050;
        const double step = 0.15;
        // Notas MIDI: 69 corresponde a La = 440 Hz. El valor 0 representa un silencio.
        int[] melody = [76, 0, 79, 83, 81, 79, 76, 74, 72, 0, 76, 79, 78, 76, 74, 71,
                        69, 0, 72, 76, 74, 72, 71, 67, 69, 71, 72, 74, 76, 74, 71, 74];
        int[] bass = [40, 40, 36, 36, 33, 33, 35, 35];
        int samples = (int)(rate * step * melody.Length);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        // Cabecera WAV de 44 bytes. Cada muestra ocupa dos bytes por ser audio de 16 bits.
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)rate;
            int tick = Math.Min(melody.Length - 1, (int)(t / step));
            double local = t % step;
            // Ataque breve y caída progresiva evitan cambios bruscos en los bordes de cada nota.
            double envelope = Math.Min(1, local / .009) * Math.Max(0, 1 - local / step);
            double frequency = 440 * Math.Pow(2, (melody[tick] - 69) / 12.0);
            // La onda cuadrada da el timbre de consola; el bajo usa una senoide más suave.
            double lead = melody[tick] == 0 ? 0 : Math.Sign(Math.Sin(2 * Math.PI * frequency * t)) * .065 * envelope;
            double bassFrequency = 440 * Math.Pow(2, (bass[tick / 4] - 69) / 12.0);
            double low = Math.Sin(2 * Math.PI * bassFrequency * t) * .09 * envelope;
            double kick = tick % 4 == 0 ? Math.Sin(2 * Math.PI * (70 * local + 3 * (1 - Math.Exp(-local * 25)))) * Math.Exp(-local * 35) * .06 : 0;
            // Sumamos las tres voces y limitamos la amplitud antes de convertir a entero PCM.
            writer.Write((short)(Math.Clamp(lead + low + kick, -1, 1) * short.MaxValue));
        }
        return stream.ToArray();
    }
    public void Dispose() { _player.Stop(); _player.Dispose(); _wave.Dispose(); }
}
