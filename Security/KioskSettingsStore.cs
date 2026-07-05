using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class KioskSettingsStore
{
    private const string FileName = "kiosk_settings.json";

    public StoredKioskSettings Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                return JsonSerializer.Deserialize<StoredKioskSettings>(json) ?? StoredKioskSettings.Empty;
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
            AuditLog.Write("SECURITY: kiosk settings failed integrity check — using empty approved-app list.");

        return StoredKioskSettings.Empty;
    }

    public void Save(StoredKioskSettings settings)
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

internal sealed class StoredKioskSettings
{
    public List<StoredKioskApp> ApprovedApps { get; set; } = [];

    public static StoredKioskSettings Empty => new();
}

internal sealed class StoredKioskApp
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Args { get; set; }
    public string? Icon { get; set; }
}
