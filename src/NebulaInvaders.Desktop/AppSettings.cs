using System.Text.Json;

namespace NebulaInvaders.Desktop;

public sealed class AppSettings
{
    public string SqlConnectionString { get; set; } = "Server=(localdb)\\MSSQLLocalDB;Database=NebulaInvaders;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5";
    public string DefaultPlayerName { get; set; } = "Piloto";
    public int PotMinimum { get; set; }
    public int PotMaximum { get; set; } = 4095;
    public bool InvertPotentiometer { get; set; }
    public bool MusicEnabled { get; set; } = true;

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NebulaInvaders");

    public static AppSettings Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("appsettings.json está vacío.")
            : new AppSettings();
        // Permite credenciales fuera del archivo distribuido. No se escriben en los registros.
        var connection = Environment.GetEnvironmentVariable("NEBULA_SQL_CONNECTION");
        if (!string.IsNullOrWhiteSpace(connection)) settings.SqlConnectionString = connection;
        if (settings.PotMinimum is < 0 or > 4095 || settings.PotMaximum is < 0 or > 4095 || settings.PotMaximum - settings.PotMinimum < 100)
            throw new InvalidDataException("La calibración requiere 0 ≤ mínimo < máximo ≤ 4095 y un intervalo de al menos 100.");
        return settings;
    }
}
