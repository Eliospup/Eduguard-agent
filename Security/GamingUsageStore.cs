using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class GamingUsageStore
{
    private static readonly string UsagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "gaming_usage.json");

    public StoredGamingUsage Load(DateOnly today)
    {
        if (!File.Exists(UsagePath))
            return StoredGamingUsage.Empty(today);

        try
        {
            var json = File.ReadAllText(UsagePath);
            var stored = JsonSerializer.Deserialize<StoredGamingUsage>(json);
            if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                return StoredGamingUsage.Empty(today);

            stored.Games ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            return stored;
        }
        catch
        {
            return StoredGamingUsage.Empty(today);
        }
    }

    public void Save(StoredGamingUsage usage)
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
            // Persistence is best-effort; ignore disk errors.
        }
    }
}

internal sealed class StoredGamingUsage
{
    public string? Date { get; set; }
    public double GlobalSeconds { get; set; }
    public Dictionary<string, double>? Games { get; set; }

    public static StoredGamingUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        GlobalSeconds = 0,
        Games = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
    };
}
