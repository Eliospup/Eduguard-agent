using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class ScreenTimeStore
{
    private static readonly string UsagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "screen_time_usage.json");

    public StoredScreenTimeUsage Load(DateOnly today)
    {
        if (!File.Exists(UsagePath))
            return StoredScreenTimeUsage.Empty(today);

        try
        {
            var json = File.ReadAllText(UsagePath);
            var stored = JsonSerializer.Deserialize<StoredScreenTimeUsage>(json);
            if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                return StoredScreenTimeUsage.Empty(today);

            return stored;
        }
        catch
        {
            return StoredScreenTimeUsage.Empty(today);
        }
    }

    public void Save(StoredScreenTimeUsage usage)
    {
        try
        {
            var dir = Path.GetDirectoryName(UsagePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(usage, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UsagePath, json);
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}

internal sealed class StoredScreenTimeUsage
{
    public string? Date { get; set; }
    public double TotalSeconds { get; set; }

    public static StoredScreenTimeUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        TotalSeconds = 0,
    };
}
