using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class LocalModeStore
{
    private const string FileName = "local_mode.json";

    // Written by LocalSettingsCatalogStore once local supervision is configured. If it exists,
    // a missing/tampered local_mode file is a "disable supervision by deleting the flag" bypass
    // rather than an online-mode install, so we fail closed to Enabled.
    private const string CatalogConfiguredMarker = ".catalog_configured";

    public StoredLocalMode Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                return JsonSerializer.Deserialize<StoredLocalMode>(json) ?? FailClosedOrDefault();
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
        {
            AuditLog.Write("SECURITY: local-mode flag failed integrity check — failing closed.");
            return FailClosedOrDefault();
        }

        // Missing: fail closed to Enabled if local supervision was ever configured.
        return FailClosedOrDefault();
    }

    public void Save(StoredLocalMode stored)
    {
        SecureStateFile.Write(FileName, JsonSerializer.Serialize(stored));
    }

    /// <summary>
    /// When the local settings catalog has been configured, a missing or tampered local-mode
    /// flag must not silently drop the machine out of supervision — assume it is still on. On a
    /// never-configured (fresh or online-only) install there is no catalog marker, so we keep
    /// the disabled default.
    /// </summary>
    private static StoredLocalMode FailClosedOrDefault() =>
        SecureStateFile.Exists(CatalogConfiguredMarker)
            ? new StoredLocalMode { Enabled = true, EnabledAt = DateTimeOffset.UtcNow }
            : StoredLocalMode.Default;

    internal sealed class StoredLocalMode
    {
        public bool Enabled { get; init; }
        public DateTimeOffset? EnabledAt { get; init; }

        public static StoredLocalMode Default => new();
    }
}
