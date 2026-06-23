using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class YoutubeUsageStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Config.AgentDataDir,
        "youtube_usage.json");

    public StoredYoutubeUsage Load(DateOnly today)
    {
        if (!File.Exists(FilePath))
            return StoredYoutubeUsage.Empty(today);

        try
        {
            var json = File.ReadAllText(FilePath);
            var stored = JsonSerializer.Deserialize<StoredYoutubeUsage>(json);
            if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                return StoredYoutubeUsage.Empty(today);

            return stored;
        }
        catch
        {
            return StoredYoutubeUsage.Empty(today);
        }
    }

    public void Save(StoredYoutubeUsage usage)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(usage, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

internal sealed class StoredYoutubeUsage
{
    public string Date { get; set; } = string.Empty;
    public double TotalSeconds { get; set; }

    public static StoredYoutubeUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        TotalSeconds = 0,
    };
}
