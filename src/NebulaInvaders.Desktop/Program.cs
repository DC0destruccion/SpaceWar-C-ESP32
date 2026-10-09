namespace NebulaInvaders.Desktop;

/// <summary>Punto de entrada: carga configuración y entrega el control al bucle de Windows Forms.</summary>
internal static class Program
{
    // Windows Forms y sus componentes de Windows necesitan un hilo principal STA.
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool preview = args.Length == 2 && args[0] == "--render-preview";
        // El mutex es único por usuario, incluso entre sesiones de Windows. Evita que
        // dos procesos escriban al mismo tiempo en la misma cola de partidas pendientes.
        string userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        using var mutex = new Mutex(true, "Global\\NebulaInvaders.Desktop." + userSid, out bool first);
        if (!first && !preview) { MessageBox.Show("Nebula Invaders ya está abierto.", "Nebula Invaders"); return 1; }
        try
        {
            var settings = AppSettings.Load();
            using var form = new MainForm(settings, preview);
            if (preview) form.RenderPreview(Path.GetFullPath(args[1]));
            else Application.Run(form); // Atiende pintura, teclado, botones y el temporizador hasta cerrar.
            return 0;
        }
        catch (Exception ex)
        {
            if (preview) { Console.Error.WriteLine(ex); return 1; }
            MessageBox.Show("No se pudo iniciar Nebula Invaders. Revisa appsettings.json y extrae completa la carpeta del juego.\n\n" + ex.GetType().Name,
                "Error de inicio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
