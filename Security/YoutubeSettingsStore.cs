using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class YoutubeSettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Config.AgentDataDir,
        "youtube_settings.json");

    public StoredYoutubeSettings Load()
    {
        if (!File.Exists(FilePath))
            return StoredYoutubeSettings.Empty;

        try
        {
            var json = File.ReadAllText(FilePath);
            var stored = JsonSerializer.Deserialize<StoredYoutubeSettings>(json);
            return stored ?? StoredYoutubeSettings.Empty;
        }
        catch
        {
            return StoredYoutubeSettings.Empty;
        }
    }

    public void Save(StoredYoutubeSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

internal sealed class StoredYoutubeSettings
{
    public int? DailyLimitMinutes { get; set; }
    public Dictionary<string, int>? WeeklyLimits { get; set; }
    public bool? ShowOverlay { get; set; }
    public bool? RestrictedModeEnabled { get; set; }

    public static StoredYoutubeSettings Empty => new();
}
