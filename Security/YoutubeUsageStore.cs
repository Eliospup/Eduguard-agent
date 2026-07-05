using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class YoutubeUsageStore
{
    private const string FileName = "youtube_usage.json";

    public StoredYoutubeUsage Load(DateOnly today)
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredYoutubeUsage>(json);
                if (stored is null || stored.Date != today.ToString("yyyy-MM-dd"))
                    return StoredYoutubeUsage.Empty(today);

                return stored;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
        {
            AuditLog.Write("SECURITY: YouTube usage failed integrity check — failing closed to limit reached.");
            return StoredYoutubeUsage.Saturated(today);
        }

        return StoredYoutubeUsage.Empty(today);
    }

    public void Save(StoredYoutubeUsage usage)
    {
        SecureStateFile.Write(FileName, JsonSerializer.Serialize(usage, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class StoredYoutubeUsage
{
    // Fail-closed sentinel: larger than any plausible daily limit.
    private const double SaturatedSeconds = 48 * 60 * 60;

    public string Date { get; set; } = string.Empty;
    public double TotalSeconds { get; set; }

    public static StoredYoutubeUsage Empty(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        TotalSeconds = 0,
    };

    public static StoredYoutubeUsage Saturated(DateOnly today) => new()
    {
        Date = today.ToString("yyyy-MM-dd"),
        TotalSeconds = SaturatedSeconds,
    };
}
