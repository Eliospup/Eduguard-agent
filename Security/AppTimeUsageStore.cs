using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class AppTimeUsageStore
{
    private static readonly string UsagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "app_time_usage.json");

    public StoredAppTimeUsage Load(DateOnly today)
    {
        if (!File.Exists(UsagePath))
            return StoredAppTimeUsage.Empty(today);

        try
        {
            var json = File.ReadAllText(UsagePath);
            var stored = JsonSerializer.Deserialize<StoredAppTimeUsage>(json);
            if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                return StoredAppTimeUsage.Empty(today);

            stored.Apps ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            return stored;
        }
        catch
        {
            return StoredAppTimeUsage.Empty(today);
        }
    }

    public void Save(StoredAppTimeUsage usage)
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
            // Persistence is best-effort.
        }
    }
}

internal sealed class StoredAppTimeUsage
{
    public string? Date { get; set; }
    public Dictionary<string, double>? Apps { get; set; }

    public static StoredAppTimeUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        Apps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
    };
}
