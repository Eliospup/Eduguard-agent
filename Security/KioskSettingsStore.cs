using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class KioskSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "kiosk_settings.json");

    public StoredKioskSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return StoredKioskSettings.Empty;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StoredKioskSettings>(json) ?? StoredKioskSettings.Empty;
        }
        catch
        {
            return StoredKioskSettings.Empty;
        }
    }

    public void Save(StoredKioskSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}

internal sealed class StoredKioskSettings
{
    public List<StoredKioskApp> ApprovedApps { get; set; } = [];

    public static StoredKioskSettings Empty => new();
}

internal sealed class StoredKioskApp
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? Icon { get; set; }
}
