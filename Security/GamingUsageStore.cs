using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class GamingUsageStore
{
    private const string FileName = "gaming_usage.json";

    public StoredGamingUsage Load(DateOnly today)
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredGamingUsage>(json);
                if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                    return StoredGamingUsage.Empty(today);

                stored.Games ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                return stored;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
        {
            AuditLog.Write("SECURITY: gaming usage failed integrity check — failing closed to limit reached.");
            return StoredGamingUsage.Saturated(today);
        }

        return StoredGamingUsage.Empty(today);
    }

    public void Save(StoredGamingUsage usage)
    {
        try
        {
            SecureStateFile.Write(FileName, JsonSerializer.Serialize(usage, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persistence is best-effort; ignore disk errors.
        }
    }
}

internal sealed class StoredGamingUsage
{
    // Fail-closed sentinel: larger than any plausible daily limit.
    private const double SaturatedSeconds = 48 * 60 * 60;

    public string? Date { get; set; }
    public double GlobalSeconds { get; set; }
    public Dictionary<string, double>? Games { get; set; }

    public static StoredGamingUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        GlobalSeconds = 0,
        Games = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
    };

    public static StoredGamingUsage Saturated(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        GlobalSeconds = SaturatedSeconds,
        Games = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
    };
}
