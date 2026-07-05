using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

internal sealed class GamingSettingsStore
{
    private const string FileName = "gaming_settings.json";

    public StoredGamingSettings Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                return JsonSerializer.Deserialize<StoredGamingSettings>(json) ?? StoredGamingSettings.Empty;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
            AuditLog.Write("SECURITY: gaming settings failed integrity check — using defaults (re-driven from the secured catalog).");

        return StoredGamingSettings.Empty;
    }

    public void Save(StoredGamingSettings settings)
    {
        try
        {
            SecureStateFile.Write(FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort persistence.
        }
    }
}

internal sealed class StoredGamingSettings
{
    public int? DailyLimitMinutes { get; set; }
    public Dictionary<string, int>? WeeklyLimits { get; set; }
    public List<StoredGamingExtraGame>? ExtraGames { get; set; }
    public List<string>? IgnoredGames { get; set; }
    public bool? ShowPlaytimeOverlay { get; set; }
    public Dictionary<string, int>? GameLimits { get; set; }

    public static StoredGamingSettings Empty => new()
    {
        ExtraGames = [],
        IgnoredGames = [],
    };
}

internal sealed class StoredGamingExtraGame
{
    public string? Exe { get; set; }
    public string? Name { get; set; }
}
