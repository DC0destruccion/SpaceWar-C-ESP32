namespace NebulaInvaders.Desktop;

/// <summary>Paleta y estilo comunes. Cambiar estos colores actualiza interfaz y escenario.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(9, 13, 25);
    public static readonly Color Panel = Color.FromArgb(16, 23, 40);
    public static readonly Color Border = Color.FromArgb(39, 53, 77);
    public static readonly Color Text = Color.FromArgb(234, 243, 251);
    public static readonly Color Muted = Color.FromArgb(151, 170, 193);
    public static readonly Color Cyan = Color.FromArgb(99, 242, 220);
    public static readonly Color Purple = Color.FromArgb(176, 149, 255);
    public static readonly Color Pink = Color.FromArgb(255, 112, 162);
    public static readonly Color Gold = Color.FromArgb(255, 206, 116);

    public static Button Button(string text, bool primary = false)
    {
        var button = new ArcadeButton
        {
            Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Cyan : Panel, ForeColor = primary ? Background : Text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), Height = 34, TabStop = true
        };
        button.FlatAppearance.BorderColor = primary ? Cyan : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(135, 255, 234) : Color.FromArgb(33, 45, 68);
        return button;
    }
}

/// <summary>Dibujo propio del botón para mantener legible el texto también al deshabilitarlo.</summary>
internal sealed class ArcadeButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var fill = Enabled ? BackColor : Theme.Panel;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var border = new Pen(Enabled ? FlatAppearance.BorderColor : Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, BackColor);
    }
}
