using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

internal sealed class GamingSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "gaming_settings.json");

    public StoredGamingSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return StoredGamingSettings.Empty;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var stored = JsonSerializer.Deserialize<StoredGamingSettings>(json);
            return stored ?? StoredGamingSettings.Empty;
        }
        catch
        {
            return StoredGamingSettings.Empty;
        }
    }

    public void Save(StoredGamingSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}

internal sealed class StoredGamingSettings
{
    public int? DailyLimitMinutes { get; set; }
    public Dictionary<string, int>? WeeklyLimits { get; set; }
    public List<StoredGamingExtraGame>? ExtraGames { get; set; }
    public List<string>? IgnoredGames { get; set; }
    public bool? ShowPlaytimeOverlay { get; set; }
    public Dictionary<string, int>? GameLimits { get; set; }

    public static StoredGamingSettings Empty => new()
    {
        ExtraGames = [],
        IgnoredGames = [],
    };
}

internal sealed class StoredGamingExtraGame
{
    public string? Exe { get; set; }
    public string? Name { get; set; }
}
