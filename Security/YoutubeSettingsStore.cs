using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class YoutubeSettingsStore
{
    private const string FileName = "youtube_settings.json";

    public StoredYoutubeSettings Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                return JsonSerializer.Deserialize<StoredYoutubeSettings>(json) ?? StoredYoutubeSettings.Empty;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
            AuditLog.Write("SECURITY: YouTube settings failed integrity check — using defaults (re-driven from the secured catalog).");

        return StoredYoutubeSettings.Empty;
    }

    public void Save(StoredYoutubeSettings settings)
    {
        SecureStateFile.Write(FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
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
