using System.Drawing.Drawing2D;
using System.Drawing.Text;
using NebulaInvaders.Core;

namespace NebulaInvaders.Desktop;

public sealed class GameCanvas : Control
{
    private readonly GameEngine _game;
    private readonly (float X, float Y, float Size, float Speed)[] _stars;
    private readonly Font _small = new("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font _body = new("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font _label = new("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _medium = new("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _title = new("Segoe UI", 54f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _mono = new("Consolas", 23f, FontStyle.Bold, GraphicsUnit.Pixel);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float AnimationTime { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string PauseReason { get; set; } = "Tómate un respiro. El espacio puede esperar.";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Esp32Mode { get; set; }
    // Cada cadena es una fila de píxeles: '1' dibuja un cuadro y '0' deja transparencia.
    // Así los invasores no dependen de imágenes externas que deban copiarse con el juego.
    private static readonly string[][] Sprites =
    [
        ["00100000100", "00010001000", "00111111100", "01101110110", "11111111111", "10111111101", "10100000101", "00011011000"],
        ["00011111000", "01111111110", "11101110111", "11111111111", "00111011100", "01100000110", "11000000011", "00110001100"],
        ["00001110000", "00011111000", "00111111100", "01101110110", "11111111111", "00101110100", "01000000010", "10000000001"]
    ];

    public GameCanvas(GameEngine game)
    {
        _game = game;
        DoubleBuffered = true; // Se compone el fotograma antes de mostrarlo para evitar parpadeos.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Background;
        Dock = DockStyle.Fill;
        var random = new Random(73);
        _stars = Enumerable.Range(0, 100).Select(_ => ((float)random.NextDouble() * 960,
            (float)random.NextDouble() * 640, 1f + (float)random.NextDouble() * 1.5f, 3f + (float)random.NextDouble() * 14)).ToArray();
    }

    ///Orden de dibujo: espacio, entidades o menú, indicadores y borde.
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        float scale = Math.Min(Width / 960f, Height / 640f);
        if (scale <= 0) return;
        g.TranslateTransform((Width - 960 * scale) / 2, (Height - 640 * scale) / 2);
        g.ScaleTransform(scale, scale);
        g.SetClip(new RectangleF(0, 0, 960, 640));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        DrawSpace(g);
        if (_game.Phase == GamePhase.Ready) DrawAttract(g);
        else DrawGame(g);
        DrawHud(g);
        DrawProgress(g);
        using var border = new Pen(Theme.Border);
        g.DrawRectangle(border, 0, 0, 959, 639);
    }

    ///Estrellas con distintas velocidades producen sensación de profundidad.
    private void DrawSpace(Graphics g)
    {
        using var background = new LinearGradientBrush(new Point(0, 0), new Point(960, 640), Color.FromArgb(12, 22, 42), Color.FromArgb(15, 13, 36));
        g.FillRectangle(background, 0, 0, 960, 640);
        using var orbital = new Pen(Color.FromArgb(15, Theme.Purple));
        g.DrawEllipse(orbital, 480, -340, 670, 670);
        g.DrawEllipse(orbital, 510, -310, 610, 610);
        using var glow = new SolidBrush(Color.FromArgb(10, Theme.Purple));
        g.FillEllipse(glow, 545, -145, 375, 375);
        foreach (var star in _stars)
        {
            float y = (star.Y + AnimationTime * star.Speed) % 640;
            int alpha = (int)(65 + 90 * (0.5 + 0.5 * Math.Sin(AnimationTime + star.X)));
            using var brush = new SolidBrush(Color.FromArgb(alpha, Theme.Text));
            g.FillEllipse(brush, star.X, y, star.Size, star.Size);
        }
        using var grid = new Pen(Color.FromArgb(13, Theme.Cyan));
        for (int x = -300; x <= 1260; x += 90) g.DrawLine(grid, 480 + (x - 480) * .3f, 515, x, 640);
        for (int y = 515; y < 640; y += 25) g.DrawLine(grid, 0, y, 960, y);
    }

    ///Muestra puntuación, sector y escudos usando los valores actuales del motor.
    private void DrawHud(Graphics g)
    {
        using var background = new SolidBrush(Color.FromArgb(215, Theme.Background));
        g.FillRectangle(background, 1, 1, 958, 65);
        DrawText(g, "PUNTUACIÓN", _label, Theme.Muted, 24, 12);
        DrawText(g, _game.Score.ToString("D6"), _mono, Theme.Cyan, 23, 28);
        DrawText(g, "SECTOR", _label, Theme.Muted, 421, 12);
        DrawText(g, $"0{_game.Level} / 05", _mono, Theme.Text, 416, 28);
        DrawText(g, "ESCUDOS", _label, Theme.Muted, 780, 12);
        for (int i = 0; i < 3; i++)
        {
            using var brush = new SolidBrush(i < _game.Lives ? Theme.Pink : Theme.Border);
            g.FillPolygon(brush, [new PointF(783 + i * 29, 35), new PointF(803 + i * 29, 35), new PointF(803 + i * 29, 45), new PointF(793 + i * 29, 53), new PointF(783 + i * 29, 45)]);
        }
        using var line = new Pen(Theme.Border);
        g.DrawLine(line, 0, 65, 960, 65);
    }

    ///Pantalla inicial animada; sus invasores decorativos no participan en colisiones.
    private void DrawAttract(Graphics g)
    {
        DrawText(g, "DEFENSA ORBITAL  /  MISIÓN 001", _label, Theme.Cyan, 0, 131, true);
        DrawText(g, "NEBULA", _title, Theme.Text, 0, 156, true);
        DrawText(g, "I N V A D E R S", _medium, Theme.Purple, 0, 222, true);
        DrawText(g, "Un potenciómetro. Una nave. Cinco sectores.", _body, Theme.Muted, 0, 277, true);
        for (int i = 0; i < 5; i++) DrawEnemy(g, 300 + i * 90, 365 + MathF.Sin(AnimationTime * 2 + i) * 7, i % 3, 3.4f, i % 2 == 0 ? Theme.Purple : Theme.Pink);
        DrawPlayer(g, 480, 473, false);
        DrawText(g, "PREPARA TU CONTROL Y PULSA INICIAR", _label, Theme.Cyan, 0, 535, true);
        DrawText(g, "Mueve la nave • Disparo automático • Evita la invasión", _body, Theme.Muted, 0, 563, true);
    }

    ///Dibuja las entidades y superpone la introducción, pausa, victoria o derrota.
    private void DrawGame(Graphics g)
    {
        foreach (var enemy in _game.Enemies)
            DrawEnemy(g, enemy.X, enemy.Y, enemy.Type % 3, enemy.Width / 11f, enemy.Row == 0 ? Theme.Pink : enemy.Row == 1 ? Theme.Purple : Theme.Cyan);
        foreach (var projectile in _game.Projectiles)
        {
            var color = projectile.FromPlayer ? Theme.Cyan : Theme.Pink;
            using var glow = new SolidBrush(Color.FromArgb(45, color));
            using var shot = new SolidBrush(color);
            g.FillEllipse(glow, projectile.X - 7, projectile.Y - 11, 14, 22);
            g.FillRectangle(shot, projectile.X - projectile.Width / 2, projectile.Y - projectile.Height / 2, projectile.Width, projectile.Height);
        }
        foreach (var particle in _game.Particles)
        {
            float strength = Math.Clamp(particle.Life / particle.MaxLife, 0f, 1f);
            Color color = particle.ColorIndex % 3 == 0 ? Theme.Cyan : particle.ColorIndex % 3 == 1 ? Theme.Pink : Theme.Gold;
            using var brush = new SolidBrush(Color.FromArgb((int)(255 * strength), color));
            g.FillRectangle(brush, particle.X - 2, particle.Y - 2, 3 + strength * 3, 3 + strength * 3);
        }
        if (_game.Lives > 0 && (_game.InvulnerabilityRemaining <= 0 || (int)(AnimationTime * 12) % 2 == 0))
            DrawPlayer(g, _game.PlayerX, _game.PlayerY, _game.InvulnerabilityRemaining > 0);

        if (_game.Phase == GamePhase.LevelIntro)
            Overlay(g, $"SECTOR 0{_game.Level}", LevelCatalog.All[_game.Level - 1].Name.ToUpperInvariant(), "Prepárate · " + Math.Ceiling(_game.LevelIntroRemaining), Theme.Cyan);
        else if (_game.Phase == GamePhase.Paused)
            Overlay(g, "EN PAUSA", PauseReason, "Pulsa REANUDAR o la tecla P", Theme.Gold);
        else if (_game.Phase == GamePhase.Won)
            Overlay(g, "GALAXIA A SALVO", "Completaste los cinco sectores", $"{_game.Score:N0} puntos · Inicia otra misión para superarte", Theme.Cyan);
        else if (_game.Phase == GamePhase.GameOver)
            Overlay(g, "FIN DE LA MISIÓN", $"Llegaste al sector 0{_game.Level}", $"{_game.Score:N0} puntos · Tu próxima misión te espera", Theme.Pink);
    }

    /// Alterna la posición de las patas según el reloj para crear dos fotogramas de animación.
    private void DrawEnemy(Graphics g, float x, float y, int type, float pixel, Color color)
    {
        var sprite = Sprites[type];
        bool frame = (int)(AnimationTime * (3 + _game.Level * .3)) % 2 == 0;
        using var glow = new SolidBrush(Color.FromArgb(14, color));
        g.FillEllipse(glow, x - 28, y - 24, 56, 48);
        using var brush = new SolidBrush(color);
        for (int row = 0; row < sprite.Length; row++)
            for (int col = 0; col < sprite[row].Length; col++)
                if (sprite[row][col] == '1')
                {
                    float offset = row >= 6 && frame ? (col < 5 ? pixel : -pixel) : 0;
                    g.FillRectangle(brush, x + (col - 5.5f) * pixel + offset, y + (row - 4) * pixel, pixel, pixel);
                }
    }

    /// Nave poligonal con llama oscilante y aro visible durante la protección temporal.
    private void DrawPlayer(Graphics g, float x, float y, bool shield)
    {
        float flame = 11 + MathF.Sin(AnimationTime * 28) * 5;
        using var thruster = new SolidBrush(Theme.Purple);
        g.FillPolygon(thruster, [new PointF(x - 6, y + 13), new PointF(x, y + 15 + flame), new PointF(x + 6, y + 13)]);
        using var ship = new SolidBrush(Theme.Cyan);
        g.FillPolygon(ship, [new PointF(x, y - 15), new PointF(x + 9, y - 1), new PointF(x + 23, y + 7), new PointF(x + 23, y + 15), new PointF(x + 6, y + 11), new PointF(x - 6, y + 11), new PointF(x - 23, y + 15), new PointF(x - 23, y + 7), new PointF(x - 9, y - 1)]);
        using var cockpit = new SolidBrush(Theme.Background);
        g.FillRectangle(cockpit, x - 3, y - 3, 6, 9);
        if (shield) { using var pen = new Pen(Color.FromArgb(140, Theme.Cyan), 2); g.DrawEllipse(pen, x - 34, y - 31, 68, 68); }
    }

    /// <summary>La franja inferior recuerda el mando seleccionado y el nivel de velocidad.</summary>
    private void DrawProgress(Graphics g)
    {
        using var bg = new SolidBrush(Color.FromArgb(220, Theme.Background));
        g.FillRectangle(bg, 0, 615, 960, 25);
        DrawText(g, Esp32Mode ? "ESP32-WROOM-32U / POTENCIÓMETRO" : "MODO PRUEBA / TECLADO ← →", _small, Theme.Muted, 17, 621);
        for (int i = 0; i < 5; i++)
        {
            using var brush = new SolidBrush(i < _game.Level ? Theme.Cyan : Theme.Border);
            g.FillRectangle(brush, 749 + i * 38, 625, 28, 3);
        }
        DrawText(g, "VELOCIDAD", _small, Theme.Muted, 678, 619);
    }

    /// <summary>Panel translúcido común a pausa, introducción y resultado final.</summary>
    private void Overlay(Graphics g, string title, string subtitle, string hint, Color accent)
    {
        using var shade = new SolidBrush(Color.FromArgb(218, Theme.Background));
        g.FillRectangle(shade, 80, 219, 800, 210);
        using var pen = new Pen(Color.FromArgb(100, accent));
        g.DrawRectangle(pen, 80, 219, 800, 210);
        using var bar = new SolidBrush(accent);
        g.FillRectangle(bar, 432, 219, 96, 3);
        DrawText(g, title, _medium, accent, 0, 259, true);
        DrawText(g, subtitle, _body, Theme.Text, 0, 313, true);
        DrawText(g, hint, _body, Theme.Muted, 0, 358, true);
    }

    private static void DrawText(Graphics g, string value, Font font, Color color, float x, float y, bool centered = false)
    {
        using var brush = new SolidBrush(color);
        if (centered) x = (960 - g.MeasureString(value, font).Width) / 2;
        g.DrawString(value, font, brush, x, y);
    }

   
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _small.Dispose(); _body.Dispose(); _label.Dispose(); _medium.Dispose(); _title.Dispose(); _mono.Dispose(); }
        base.Dispose(disposing);
    }
}
