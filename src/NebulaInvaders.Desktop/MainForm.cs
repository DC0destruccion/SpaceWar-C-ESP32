using System.Diagnostics;
using System.Runtime.InteropServices;
using NebulaInvaders.Core;
using NebulaInvaders.Data;
using NebulaInvaders.Desktop.Audio;
using NebulaInvaders.Desktop.Hardware;

namespace NebulaInvaders.Desktop;

public sealed class MainForm : Form
{
    private readonly GameEngine _game = new();
    private readonly GameCanvas _canvas;
    private readonly AppSettings _settings;
    private readonly PotentiometerController _controller = new();
    private readonly SqlGameRepository _repository;
    private readonly PendingGameStore _outbox;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    // Serializa preparar la base, guardar y consultar para evitar operaciones superpuestas.
    private readonly SemaphoreSlim _databaseGate = new(1, 1);
    private readonly TextBox _player = new();
    private readonly ComboBox _mode = new(), _ports = new();
    private readonly NumericUpDown _minimum = new(), _maximum = new();
    private readonly CheckBox _invert = new(), _musicCheck = new();
    private readonly Label _deviceStatus = new(), _databaseStatus = new(), _footer = new();
    private readonly ProgressBar _potValue = new();
    private readonly Button _connect = Theme.Button("Conectar ESP32"), _refresh = Theme.Button("↻");
    private readonly Button _start = Theme.Button("INICIAR MISIÓN", true), _pause = Theme.Button("Pausa  ·  P");
    private readonly Button _prepareSql = Theme.Button("Preparar SQL"), _ranking = Theme.Button("Ranking");
    private SynthMusic? _music;
    private double _lastTime;
    private float _keyboardPosition = .5f;
    private bool _sessionStarted, _resultQueued, _closing, _canClose, _databaseBusy;
    private DateTime _startedUtc;
    private string _sessionPlayer = "Piloto", _sessionControl = "Teclado";
    // Conservamos la tarea para que cerrar la ventana espere al guardado que ya comenzó.
    private Task _saveTask = Task.CompletedTask;
    // Solo se libera este resultado cuando la escritura en la cola local confirma éxito.
    private GameResult? _pendingResult;
    private bool UsingEsp32 => _mode.SelectedIndex == 0;
    private bool ActiveGame => _game.Phase is GamePhase.Playing or GamePhase.LevelIntro or GamePhase.Paused;

    // Consulta si una tecla sigue pulsada, sin depender de la repetición del teclado de Windows.
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    /// <summary>Construye los controles y enlaza eventos; preview omite música y temporizador automáticos.</summary>
    public MainForm(AppSettings settings, bool preview = false)
    {
        _settings = settings;
        _repository = new SqlGameRepository(settings.SqlConnectionString);
        _outbox = new PendingGameStore(Path.Combine(AppSettings.DataDirectory, "pending-games.json"));
        _canvas = new GameCanvas(_game);
        Text = "Nebula Invaders · ESP32-WROOM-32U";
        var workingArea = Screen.FromControl(this).WorkingArea;
        ClientSize = preview ? new Size(1280, 880) : new Size(Math.Min(1280, workingArea.Width - 50), Math.Min(880, workingArea.Height - 85));
        MinimumSize = new Size(1050, 650);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        BuildLayout();
        _player.Text = string.IsNullOrWhiteSpace(settings.DefaultPlayerName) ? "Piloto" : settings.DefaultPlayerName[..Math.Min(32, settings.DefaultPlayerName.Length)];
        _minimum.Value = settings.PotMinimum;
        _maximum.Value = settings.PotMaximum;
        _invert.Checked = settings.InvertPotentiometer;
        _mode.SelectedIndex = 1;
        _musicCheck.Checked = settings.MusicEnabled;
        RefreshPorts();
        _connect.Click += (_, _) => ToggleConnection();
        _refresh.Click += (_, _) => RefreshPorts();
        _mode.SelectedIndexChanged += (_, _) => UpdateControls();
        _start.Click += (_, _) => StartGame();
        _pause.Click += (_, _) => TogglePause();
        _prepareSql.Click += async (_, _) => await PrepareDatabaseAsync();
        _ranking.Click += async (_, _) => await ShowRankingAsync();
        _musicCheck.CheckedChanged += (_, _) => UpdateMusic();
        _timer.Tick += (_, _) => TickGame();
        Deactivate += (_, _) => PauseFor("La ventana perdió el foco.");
        FormClosing += ClosingAsync;
        Shown += async (_, _) =>
        {
            if (preview) return;
            try { _music = new SynthMusic(); UpdateMusic(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { _musicCheck.Checked = false; _musicCheck.Enabled = false; _footer.Text = "Audio no disponible. Puedes jugar sin música."; }
            try { await PrepareDatabaseAsync(); }
            catch (Exception) { SetDatabaseStatus("No se puede leer la cola local. Revisa pending-games.json antes de jugar.", Theme.Pink); }
            if (_closing || IsDisposed) return;
            _lastTime = _clock.Elapsed.TotalSeconds;
            _timer.Start();
        };
        UpdateControls();
    }

    /// <summary>Distribuye cabecera, panel lateral, escenario escalable y ayuda inferior.</summary>
    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(20, 0, 20, 0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.Controls.Add(new Label { Text = "N / I", Font = new Font("Consolas", 23, FontStyle.Bold), ForeColor = Theme.Cyan, Location = new Point(0, 19), AutoSize = true });
        header.Controls.Add(new Label { Text = "NEBULA INVADERS", Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(119, 15), AutoSize = true });
        header.Controls.Add(new Label { Text = "ARCADE ESPACIAL  /  CONTROL ANALÓGICO", Font = new Font("Segoe UI", 8), ForeColor = Theme.Muted, Location = new Point(121, 46), AutoSize = true });
        var badge = new Label { Text = "ESP32-32U   /   05 SECTORES", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right, Width = 285, ForeColor = Theme.Cyan, Font = new Font("Consolas", 10) };
        header.Controls.Add(badge);
        root.Controls.Add(header, 0, 0);
        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 274));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(middle, 0, 1);
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0, 0, 18, 0), AutoScroll = true };
        middle.Controls.Add(sidebar, 0, 0);
        middle.Controls.Add(_canvas, 1, 0);

        AddLabel(sidebar, "CENTRO DE MANDO", 18, 18, 220, Theme.Cyan, true);
        AddLabel(sidebar, "PILOTO", 18, 50, 210);
        ConfigureBox(_player, 18, 71, 220, 29); _player.MaxLength = 32; sidebar.Controls.Add(_player);
        AddLabel(sidebar, "CONTROL DE VUELO", 18, 111, 220);
        ConfigureBox(_mode, 18, 132, 220, 30); _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Items.AddRange(["ESP32-32U · Potenciómetro", "Prueba · Teclado ← →"]); sidebar.Controls.Add(_mode);
        ConfigureBox(_ports, 18, 174, 176, 28); _ports.DropDownStyle = ComboBoxStyle.DropDownList; sidebar.Controls.Add(_ports);
        Place(sidebar, _refresh, 202, 172, 36, 30);
        Place(sidebar, _connect, 18, 211, 220, 34);
        _deviceStatus.ForeColor = Theme.Muted; _deviceStatus.Font = new Font("Segoe UI", 8.5f);
        _deviceStatus.Text = "Modo de prueba sin placa.\nUsa ← → o A / D para moverte.";
        Place(sidebar, _deviceStatus, 18, 255, 220, 38);
        _potValue.Minimum = 0; _potValue.Maximum = 4095; _potValue.Value = 2048;
        Place(sidebar, _potValue, 18, 296, 220, 5);
        AddLabel(sidebar, "ADC MÍN.", 18, 315, 98);
        AddLabel(sidebar, "ADC MÁX.", 132, 315, 106);
        foreach (var numeric in new[] { _minimum, _maximum }) { numeric.Minimum = 0; numeric.Maximum = 4095; numeric.Increment = 10; numeric.BackColor = Theme.Background; numeric.ForeColor = Theme.Text; numeric.BorderStyle = BorderStyle.FixedSingle; }
        Place(sidebar, _minimum, 18, 337, 102, 28); Place(sidebar, _maximum, 132, 337, 106, 28);
        _invert.Text = "Invertir giro"; _invert.AutoSize = true; Place(sidebar, _invert, 18, 374, 210, 24);
        var divider = new Panel { BackColor = Theme.Border }; Place(sidebar, divider, 18, 411, 220, 1);
        _musicCheck.Text = "Música chiptune"; Place(sidebar, _musicCheck, 18, 423, 220, 26);
        Place(sidebar, _start, 18, 468, 220, 46);
        Place(sidebar, _pause, 18, 524, 220, 34);
        AddLabel(sidebar, "REGISTRO DE MISIONES", 18, 584, 220, Theme.Cyan, true);
        Place(sidebar, _prepareSql, 18, 611, 128, 34); Place(sidebar, _ranking, 154, 611, 84, 34);
        _databaseStatus.Font = new Font("Segoe UI", 8.5f); _databaseStatus.ForeColor = Theme.Muted;
        Place(sidebar, _databaseStatus, 18, 655, 220, 77);
        _databaseStatus.Text = "SQL Server Express\nPulsa Preparar SQL para crear la base y sincronizar partidas.";
        _footer.Text = "GIRA PARA MOVERTE  ·  DISPARO AUTOMÁTICO          P  PAUSA     M  MÚSICA";
        _footer.Dock = DockStyle.Fill; _footer.TextAlign = ContentAlignment.MiddleLeft; _footer.ForeColor = Theme.Muted; _footer.Font = new Font("Segoe UI", 8);
        root.Controls.Add(_footer, 0, 2);
    }

    private static void Place(Control parent, Control child, int x, int y, int width, int height)
    { child.SetBounds(x, y, width, height); parent.Controls.Add(child); }
    private static void ConfigureBox(Control box, int x, int y, int width, int height)
    { box.SetBounds(x, y, width, height); box.BackColor = Theme.Background; box.ForeColor = Theme.Text; box.Font = new Font("Segoe UI", 10); }
    private static void AddLabel(Control parent, string text, int x, int y, int width, Color? color = null, bool bold = false)
    { Place(parent, new Label { Text = text, ForeColor = color ?? Theme.Muted, Font = new Font("Segoe UI", 8, bold ? FontStyle.Bold : FontStyle.Regular) }, x, y, width, 20); }

    /// <summary>Actualiza la lista de puertos COM, conservando la selección si todavía existe.</summary>
    private void RefreshPorts()
    {
        string? previous = _ports.SelectedItem as string;
        _ports.Items.Clear();
        try { _ports.Items.AddRange(PotentiometerController.GetPorts()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _deviceStatus.Text = "No se pudo consultar los puertos COM."; }
        if (previous != null && _ports.Items.Contains(previous)) _ports.SelectedItem = previous;
        else if (_ports.Items.Count > 0) _ports.SelectedIndex = 0;
    }

    /// <summary>Abre o libera el COM. Desconectar durante una misión primero la pone en pausa.</summary>
    private void ToggleConnection()
    {
        if (_closing) return;
        if (_controller.IsOpen)
        {
            PauseFor("ESP32 desconectado. Reconecta para continuar.");
            _controller.Disconnect();
        }
        else
        {
            if (_ports.SelectedItem is not string port) { _deviceStatus.Text = "Conecta la placa USB y pulsa ↻."; return; }
            try { _controller.Connect(port); _deviceStatus.Text = "Esperando datos del potenciómetro…"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { _deviceStatus.Text = "No se pudo abrir el puerto. Cierra el monitor serie y vuelve a intentar."; }
        }
        UpdateControls();
    }

    // Un intervalo demasiado pequeño amplificaría el ruido del potenciómetro.
    private bool ValidCalibration()
    {
        bool valid = _maximum.Value - _minimum.Value >= 100;
        if (!valid) _deviceStatus.Text = "ADC máximo − mínimo debe ser ≥ 100.";
        return valid;
    }

    /// <summary>Valida nombre y mando, captura los datos de la sesión e inicia desde el nivel 1.</summary>
    private void StartGame()
    {
        if (_closing || ActiveGame || !_saveTask.IsCompleted || _pendingResult != null) return;
        if (string.IsNullOrWhiteSpace(_player.Text)) { _player.Focus(); _deviceStatus.Text = "Escribe el nombre del piloto."; return; }
        if (_player.Text.Any(char.IsControl)) { _deviceStatus.Text = "El nombre no puede contener caracteres de control."; _player.Focus(); return; }
        if (UsingEsp32 && (!_controller.HasFreshData || !ValidCalibration()))
        { _deviceStatus.Text = "Conecta el ESP32 y verifica la calibración antes de iniciar."; return; }
        _sessionStarted = true; _resultQueued = false;
        _startedUtc = DateTime.UtcNow; _sessionPlayer = _player.Text.Trim();
        _sessionControl = UsingEsp32 ? "ESP32-WROOM-32U" : "Teclado";
        _keyboardPosition = .5f;
        _canvas.Esp32Mode = UsingEsp32;
        _game.Start();
        _lastTime = _clock.Elapsed.TotalSeconds;
        UpdateControls();
        _canvas.Focus();
    }

    /// <summary>
    /// Bucle de juego: lee entrada → avanza el motor → detecta el final → solicita redibujar.
    /// El intervalo de 16 ms es aproximado; Stopwatch proporciona el tiempo real entre llamadas.
    /// </summary>
    private void TickGame()
    {
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastTime, 0, .1);
        _lastTime = now;
        _canvas.AnimationTime += dt;
        float input = _keyboardPosition;
        if (UsingEsp32)
        {
            if (_controller.HasFreshData && _maximum.Value - _minimum.Value >= 100)
            {
                input = PotentiometerProtocol.Normalize(_controller.RawValue, (int)_minimum.Value, (int)_maximum.Value, _invert.Checked);
                _potValue.Value = _controller.RawValue;
                _deviceStatus.Text = $"● Señal activa  ·  ADC {_controller.RawValue}\nPosición {input:P0}  /  115200 baudios";
                _deviceStatus.ForeColor = Theme.Cyan;
            }
            else
            {
                PauseFor("Sin señal del ESP32. Reconecta y pulsa REANUDAR.");
                _deviceStatus.Text = _controller.Error ?? (_controller.IsOpen ? "Esperando datos POT:0…4095\nRevisa el cable y el firmware." : "Conecta la placa y selecciona su puerto.");
                _deviceStatus.ForeColor = Theme.Gold;
            }
        }
        else
        {
            if (ContainsFocus && _game.Phase is GamePhase.Playing or GamePhase.LevelIntro)
            {
                if ((GetAsyncKeyState((int)Keys.Left) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.A) & 0x8000) != 0) _keyboardPosition -= dt * .85f;
                if ((GetAsyncKeyState((int)Keys.Right) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.D) & 0x8000) != 0) _keyboardPosition += dt * .85f;
                _keyboardPosition = Math.Clamp(_keyboardPosition, 0, 1);
                input = _keyboardPosition;
            }
            _deviceStatus.Text = "Modo de prueba sin placa.\nUsa ← → o A / D para moverte.";
            _deviceStatus.ForeColor = Theme.Muted;
        }
        var before = _game.Phase;
        _game.Update(dt, input);
        // Esta bandera evita guardar la misma partida en cada fotograma de la pantalla final.
        if (_game.Phase is GamePhase.Won or GamePhase.GameOver && !_resultQueued)
        {
            _resultQueued = true;
            _pendingResult = CreateResult();
            _saveTask = SaveResultAsync();
        }
        if (before != _game.Phase) UpdateControls();
        _canvas.Invalidate();
    }

    /// <summary>Pausa por pérdida de foco, mando o apertura de una función del menú.</summary>
    private void PauseFor(string reason)
    {
        if (_game.Phase is not (GamePhase.Playing or GamePhase.LevelIntro)) return;
        _game.TogglePause(); _canvas.PauseReason = reason; UpdateControls();
    }

    /// <summary>No permite reanudar con un ESP32 sin señal o una calibración inválida.</summary>
    private void TogglePause()
    {
        if (_closing || !ActiveGame) return;
        if (_game.Phase == GamePhase.Paused && UsingEsp32 && (!_controller.HasFreshData || !ValidCalibration()))
        { _canvas.PauseReason = "Necesitas señal del ESP32 y una calibración válida."; return; }
        _canvas.PauseReason = "Tómate un respiro. El espacio puede esperar.";
        _game.TogglePause(); UpdateControls();
    }

    // Interceptar las flechas impide que Windows las use para cambiar el foco entre botones.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveGame && keyData == Keys.P) { TogglePause(); return true; }
        if (ActiveGame && keyData == Keys.M) { _musicCheck.Checked = !_musicCheck.Checked; return true; }
        if (ActiveGame && keyData is Keys.Left or Keys.Right or Keys.A or Keys.D) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Habilita controles según el estado; no se cambia de piloto o mando en pleno combate.</summary>
    private void UpdateControls()
    {
        if (_closing) { Enabled = false; return; }
        Enabled = true;
        bool editable = !ActiveGame;
        _player.Enabled = editable; _mode.Enabled = editable;
        _ports.Enabled = UsingEsp32 && !_controller.IsOpen;
        _refresh.Enabled = UsingEsp32 && !_controller.IsOpen;
        _connect.Enabled = UsingEsp32;
        _connect.Text = _controller.IsOpen ? "Desconectar ESP32" : "Conectar ESP32";
        _minimum.Enabled = _maximum.Enabled = _invert.Enabled = UsingEsp32 && (editable || _game.Phase == GamePhase.Paused);
        _start.Enabled = editable && _saveTask.IsCompleted && _pendingResult == null;
        _start.Text = _sessionStarted ? "NUEVA MISIÓN" : "INICIAR MISIÓN";
        _pause.Enabled = ActiveGame;
        _pause.Text = _game.Phase == GamePhase.Paused ? "REANUDAR  ·  P" : "Pausa  ·  P";
        _prepareSql.Enabled = _ranking.Enabled = !_databaseBusy;
        _canvas.Esp32Mode = UsingEsp32;
    }

    /// <summary>Enciende o apaga el bucle musical sin detener la partida si falla el dispositivo.</summary>
    private void UpdateMusic()
    {
        try { _music?.SetEnabled(_musicCheck.Checked); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { _footer.Text = "El dispositivo de audio no está disponible."; }
    }

    // Se guarda un resultado, no el estado completo para reanudar. El UUID identifica
    // el mismo resultado durante todos los reintentos de envío a SQL Server.
    private GameResult CreateResult() => new(Guid.NewGuid(), _sessionPlayer, _game.Score, _game.Level,
        _game.Phase == GamePhase.Won, (int)_game.ElapsedSeconds, _sessionControl, _startedUtc, DateTime.UtcNow);

    /// <summary>Guarda primero en disco y luego intenta SQL; un fallo de conexión deja el registro pendiente.</summary>
    private async Task SaveResultAsync()
    {
        if (_pendingResult == null) return;
        try
        {
            // Primero disco, después SQL. Un fallo de red no pierde la puntuación.
            await _outbox.EnqueueAsync(_pendingResult);
            _pendingResult = null;
            SetDatabaseStatus("Resultado guardado en cola local. Enviando a SQL…", Theme.Gold);
        }
        catch (Exception)
        {
            SetDatabaseStatus("No se pudo guardar en disco. Libera espacio y pulsa Preparar SQL para reintentar.", Theme.Pink);
            UpdateControls(); return;
        }
        await _databaseGate.WaitAsync();
        try
        {
            // Prepara automáticamente la base y la tabla antes de enviar resultados pendientes.
            await _repository.InitializeAsync();
            int count = await _outbox.FlushAsync(_repository);
            SetDatabaseStatus($"✓ {count} partida(s) guardadas en SQL Server.", Theme.Cyan);
        }
        catch (Exception) { SetDatabaseStatus("SQL no disponible. Resultado conservado en este equipo. Pulsa Preparar SQL para reintentar.", Theme.Gold); }
        finally { _databaseGate.Release(); if (!_closing) BeginInvoke((Action)UpdateControls); }
    }

    /// <summary>Al iniciar o pulsar Preparar SQL, crea el esquema y reenvía la cola local.</summary>
    private async Task PrepareDatabaseAsync()
    {
        if (_closing || _databaseBusy) return;
        PauseFor("Configuración de SQL Server.");
        _databaseBusy = true; UpdateControls();
        await _databaseGate.WaitAsync();
        try
        {
            SetDatabaseStatus("Conectando y preparando NebulaInvaders…", Theme.Muted);
            if (_pendingResult != null) { await _outbox.EnqueueAsync(_pendingResult); _pendingResult = null; }
            await _repository.InitializeAsync();
            int count = await _outbox.FlushAsync(_repository);
            SetDatabaseStatus($"✓ SQL Server conectado.\n{count} partida(s) sincronizadas.", Theme.Cyan);
        }
        catch (Exception)
        {
            SetDatabaseStatus("No se pudo preparar SQL. Revisa la instancia, los permisos y appsettings.json. La cola local se conserva.", Theme.Gold);
        }
        finally { _databaseGate.Release(); _databaseBusy = false; if (!_closing) UpdateControls(); }
    }

    /// <summary>Consulta las diez mejores misiones y convierte sus fechas UTC a hora local para mostrarlas.</summary>
    private async Task ShowRankingAsync()
    {
        if (_closing || _databaseBusy) return;
        PauseFor("Consulta del ranking.");
        _databaseBusy = true; UpdateControls();
        await _databaseGate.WaitAsync();
        try
        {
            // Consultar el ranking también recupera partidas que quedaron sin conexión.
            await _repository.InitializeAsync();
            await _outbox.FlushAsync(_repository);
            var entries = await _repository.GetLeaderboardAsync(10);
            if (_closing) return;
            using var dialog = new Form { Text = "Mejores misiones · SQL Server", Size = new Size(730, 460), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Font, MinimizeBox = false, MaximizeBox = false };
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            grid.DefaultCellStyle.BackColor = Theme.Panel; grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Border; grid.DefaultCellStyle.SelectionForeColor = Theme.Cyan;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Background; grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Cyan;
            grid.RowTemplate.Height = 32;
            foreach (var title in new[] { "#", "Piloto", "Puntos", "Sector", "Resultado", "Fecha local" }) grid.Columns.Add(title, title);
            int place = 1;
            foreach (var entry in entries) grid.Rows.Add(place++, entry.PlayerName, entry.Score, entry.LevelReached, entry.Completed ? "Victoria" : "Finalizada", entry.EndedUtc.ToLocalTime().ToString("dd/MM/yy HH:mm"));
            dialog.Controls.Add(grid);
            if (entries.Count == 0) dialog.Controls.Add(new Label { Text = "Todavía no hay partidas. Completa tu primera misión.", Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleCenter });
            dialog.ShowDialog(this);
        }
        catch (Exception) { SetDatabaseStatus("Ranking no disponible. Pulsa Preparar SQL y verifica la conexión.", Theme.Gold); }
        finally { _databaseGate.Release(); _databaseBusy = false; if (!_closing) UpdateControls(); }
    }

    private void SetDatabaseStatus(string message, Color color)
    { if (_canClose || IsDisposed) return; _databaseStatus.Text = message; _databaseStatus.ForeColor = color; }

    /// <summary>
    /// Cancela el primer cierre, guarda el resultado parcial y vuelve a cerrar con _canClose.
    /// Así no se destruyen los controles mientras una operación asíncrona aún los necesita.
    /// </summary>
    private async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _timer.Stop(); PauseFor("Cerrando misión."); UpdateControls();
        bool capturedActiveSession = false;
        try
        {
            await _saveTask;
            if (_sessionStarted && !_resultQueued) { _pendingResult = CreateResult(); _resultQueued = true; capturedActiveSession = true; }
            if (_pendingResult != null) { await _outbox.EnqueueAsync(_pendingResult); _pendingResult = null; }
            // Las operaciones SQL iniciadas desde la interfaz terminan antes de destruir sus controles.
            await _databaseGate.WaitAsync(); _databaseGate.Release();
            _canClose = true; Close();
        }
        catch (Exception)
        {
            if (capturedActiveSession && _pendingResult != null) { _pendingResult = null; _resultQueued = false; }
            _closing = false; UpdateControls(); _timer.Start();
            MessageBox.Show(this, "No se pudo conservar la partida en disco. Libera espacio o revisa los permisos de %LOCALAPPDATA%\\NebulaInvaders y vuelve a cerrar.", "Partida pendiente de guardar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Prueba de interfaz ejecutable con --render-preview: simula una misión y genera tres PNG.
    /// No abre un puerto físico, no envía resultados a SQL ni escribe en la cola del usuario.
    /// </summary>
    public void RenderPreview(string directory)
    {
        Directory.CreateDirectory(directory);
        ShowInTaskbar = false; Opacity = 0; Show(); Application.DoEvents(); PerformLayout();
        if (!_start.Enabled || _pause.Enabled) throw new InvalidOperationException("Estado inicial de controles incorrecto.");
        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "menu.png")); }
        StartGame();
        if (_game.Phase != GamePhase.LevelIntro || _start.Enabled || _mode.Enabled || !_pause.Enabled)
            throw new InvalidOperationException("La misión no activó los controles esperados.");
        _pause.PerformClick();
        if (_game.Phase != GamePhase.Paused) throw new InvalidOperationException("El botón Pausa no detuvo la introducción.");
        _pause.PerformClick();
        if (_game.Phase != GamePhase.LevelIntro) throw new InvalidOperationException("El botón Reanudar no restauró la introducción.");
        for (int i = 0; i < 420; i++) { _game.Update(1f / 60, .5f + .38f * MathF.Sin(i * .025f)); _canvas.AnimationTime += 1f / 60; }
        if (_game.Phase != GamePhase.Playing || _game.Score <= 0) throw new InvalidOperationException("La simulación visual no avanzó.");
        _canvas.Invalidate();
        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "gameplay.png")); }
        _game.TogglePause();
        UpdateControls();
        using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, "pause.png")); }
        _pause.PerformClick();
        _mode.SelectedIndex = 0;
        TickGame();
        if (_game.Phase != GamePhase.Paused) throw new InvalidOperationException("La pérdida del mando no pausó la misión.");
        _pause.PerformClick();
        if (_game.Phase != GamePhase.Paused) throw new InvalidOperationException("Se reanudó una misión sin señal ESP32.");
        byte[] wave = SynthMusic.CreateWave();
        if (System.Text.Encoding.ASCII.GetString(wave, 0, 4) != "RIFF" || BitConverter.ToInt32(wave, 40) != wave.Length - 44)
            throw new InvalidOperationException("Cabecera de música WAV incorrecta.");
        using (var audio = new SynthMusic()) { /* Carga el WAV sin reproducirlo durante la prueba. */ }
        Console.WriteLine("PASS Desktop: menú, inicio, controles, pausa/reanudación, señal ESP32 ausente, WAV y tres renders.");
        _canClose = true;
        Hide();
    }

    // Liberar recursos permite que otro programa vuelva a usar el COM y detiene el audio.
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _controller.Dispose(); _music?.Dispose(); _databaseGate.Dispose(); }
        base.Dispose(disposing);
    }
}
