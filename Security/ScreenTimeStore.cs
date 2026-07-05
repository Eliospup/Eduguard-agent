using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class ScreenTimeStore
{
    private const string FileName = "screen_time_usage.json";

    public StoredScreenTimeUsage Load(DateOnly today)
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredScreenTimeUsage>(json);
                if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                    return StoredScreenTimeUsage.Empty(today);

                return stored;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
        {
            AuditLog.Write("SECURITY: screen-time usage failed integrity check — failing closed to limit reached.");
            return StoredScreenTimeUsage.Saturated(today);
        }

        return StoredScreenTimeUsage.Empty(today);
    }

    public void Save(StoredScreenTimeUsage usage)
    {
        try
        {
            SecureStateFile.Write(FileName, JsonSerializer.Serialize(usage, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}

internal sealed class StoredScreenTimeUsage
{
    // Fail-closed sentinel: 48h in seconds, larger than any plausible daily limit, so a
    // tampered ledger is treated as "already fully consumed" instead of resetting to zero.
    private const double SaturatedSeconds = 48 * 60 * 60;

    public string? Date { get; set; }
    public double TotalSeconds { get; set; }

    public static StoredScreenTimeUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        TotalSeconds = 0,
    };

    public static StoredScreenTimeUsage Saturated(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        TotalSeconds = SaturatedSeconds,
    };
}
