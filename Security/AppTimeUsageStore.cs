using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class AppTimeUsageStore
{
    private const string FileName = "app_time_usage.json";

    public StoredAppTimeUsage Load(DateOnly today)
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredAppTimeUsage>(json);
                if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                    return StoredAppTimeUsage.Empty(today);

                stored.Apps ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                return stored;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
        {
            // Per-app usage is keyed by arbitrary exe names we can't reconstruct here, so we
            // can't saturate it the way the single-total ledgers do. The hardened folder ACL
            // (Users read-only) is the real guard against resetting this; log the tamper.
            AuditLog.Write("SECURITY: app-time usage failed integrity check.");
        }

        return StoredAppTimeUsage.Empty(today);
    }

    public void Save(StoredAppTimeUsage usage)
    {
        try
        {
            SecureStateFile.Write(FileName, JsonSerializer.Serialize(usage, new JsonSerializerOptions { WriteIndented = true }));
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
