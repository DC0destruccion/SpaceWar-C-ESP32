using System.Diagnostics;
using System.IO.Ports;

namespace NebulaInvaders.Desktop.Hardware;

public sealed class PotentiometerController : IDisposable
{
    private SerialPort? _port;
    private CancellationTokenSource? _cancellation;
    private Task? _reader;
    private int _raw = 2048;
    private long _lastSample;
    private string? _error;
    private readonly object _sampleGate = new();
    private int _generation;
    // Volatile/Interlocked hacen visibles al hilo de interfaz las actualizaciones del lector USB.
    public int RawValue => Volatile.Read(ref _raw);
    public string? Error => Volatile.Read(ref _error);
    public bool IsOpen => _port?.IsOpen == true;
    // Abrir un COM no demuestra que el mando envíe datos
    public bool HasFreshData => IsOpen && Interlocked.Read(ref _lastSample) > 0 &&
        Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastSample)).TotalSeconds < 1.2;

    public static string[] GetPorts() => SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Connect(string portName)
    {
        Disconnect();
        var port = new SerialPort(portName, 115200)
        {
            // En DevKitC, DTR y RTS intervienen en el reinicio automático y el modo de carga.
            ReadTimeout = 150, WriteTimeout = 150, NewLine = "\n", DtrEnable = false, RtsEnable = false,
            Handshake = Handshake.None, DataBits = 8, Parity = Parity.None, StopBits = StopBits.One,
            ReadBufferSize = 4096
        };
        try { port.Open(); }
        catch { port.Dispose(); throw; }
        _port = port;
        Volatile.Write(ref _error, null);
        Interlocked.Exchange(ref _lastSample, 0);
        _cancellation = new CancellationTokenSource();
        var cancellation = _cancellation.Token;
        int generation = _generation;
        _reader = Task.Run(() => ReadLoop(port, cancellation, generation));
    }

    private void ReadLoop(SerialPort port, CancellationToken token, int generation)
    {
        // Acumulador acotado: tolera fragmentación USB y descarta ruido de arranque.
        char[] line = new char[32];
        int count = 0;
        bool overflow = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                int next = port.ReadChar();
                if (next < 0) continue;
                if (next == '\n')
                {
                    if (!overflow && PotentiometerProtocol.TryParse(new string(line, 0, count), out int raw))
                    {
                        lock (_sampleGate)
                        {
                            if (generation != _generation || token.IsCancellationRequested) return;
                            Volatile.Write(ref _raw, raw);
                            Interlocked.Exchange(ref _lastSample, Stopwatch.GetTimestamp());
                        }
                    }
                    count = 0;
                    overflow = false;
                }
                else if (count < line.Length) line[count++] = (char)next;
                else overflow = true;
            }
            catch (TimeoutException) { }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                lock (_sampleGate)
                    if (generation == _generation && !token.IsCancellationRequested) Volatile.Write(ref _error, "Se perdió el puerto serie. Reconecta el ESP32.");
                break;
            }
        }
    }

    public void Disconnect()
    {
        _cancellation?.Cancel();
        lock (_sampleGate) { _generation++; Interlocked.Exchange(ref _lastSample, 0); }
        // Cerrar desbloquea ReadChar; 
        try { _port?.Close(); } catch (IOException) { }
        try { _reader?.Wait(600); } catch (AggregateException) { }
        _port?.Dispose();
        _port = null;
        _reader = null;
        _cancellation?.Dispose();
        _cancellation = null;
        Interlocked.Exchange(ref _lastSample, 0);
    }
    public void Dispose() => Disconnect();
}
