using System.Text.Json;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Security;

/// <summary>
/// Persists the per-mode supervision rules (screen time, gaming, bedtime, system-tool locks,
/// blocked apps/sites, discipline weights…). This is security-critical: an over-the-shoulder
/// user who could edit it in Notepad would relax every restriction, so it lives in the
/// hardened <see cref="SecureDataPaths"/> folder (Users read-only) and is written through
/// <see cref="StateProtection"/> (DPAPI-encrypted + tamper-evident). Any edit to the ciphertext
/// trips an integrity failure on read, and we fail closed to the most restrictive posture
/// (Restricted Sub) instead of trusting an attacker-supplied value.
/// </summary>
internal sealed class LocalSettingsCatalogStore
{
    private static string SettingsPath => SecureDataPaths.PathFor("local_settings_catalog.json");

    // Records that the catalog has been written at least once. Once present, a missing catalog
    // is treated as a delete-to-get-permissive-defaults bypass rather than a fresh install.
    private static string ConfiguredMarkerPath => SecureDataPaths.PathFor(".catalog_configured");

    public LocalSettingsCatalog Load()
    {
        var status = StateProtection.TryRead(SettingsPath, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var catalog = JsonSerializer.Deserialize<LocalSettingsCatalog>(json);
                if (catalog is not null)
                {
                    EnsureAllModes(catalog);
                    MarkConfigured();
                    return catalog;
                }
            }
            catch
            {
                // Valid envelope but unparseable contents — treat as tampering below.
            }

            status = StateReadStatus.Tampered;
        }

        if (status == StateReadStatus.Tampered)
        {
            AuditLog.Write("SECURITY: local settings catalog failed integrity check — failing closed to Restricted Sub.");
            return CreateFailClosed();
        }

        // Missing.
        if (HasBeenConfigured())
        {
            AuditLog.Write("SECURITY: local settings catalog missing after setup — failing closed to Restricted Sub.");
            return CreateFailClosed();
        }

        return LocalSettingsCatalog.CreateDefaults();
    }

    public void Save(LocalSettingsCatalog catalog)
    {
        EnsureAllModes(catalog);
        StateProtection.Write(SettingsPath, JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
        MarkConfigured();
    }

    private static bool HasBeenConfigured() => File.Exists(ConfiguredMarkerPath);

    private static void MarkConfigured()
    {
        try
        {
            if (!File.Exists(ConfiguredMarkerPath))
                StateProtection.Write(ConfiguredMarkerPath, "1");
        }
        catch
        {
            // Best-effort; failure only weakens the delete-to-bypass guard, never blocks startup.
        }
    }

    /// <summary>
    /// Safe posture applied when the stored catalog is missing-after-setup or tampered:
    /// the defaults with the active mode forced to Restricted Sub, whose per-mode rule set
    /// already carries the most locked-down features.
    /// </summary>
    private static LocalSettingsCatalog CreateFailClosed()
    {
        var catalog = LocalSettingsCatalog.CreateDefaults();
        catalog.ActiveModeSlug = AgentModeSlugs.RestrictedSub;
        return catalog;
    }

    private static void EnsureAllModes(LocalSettingsCatalog catalog)
    {
        foreach (var mode in AgentModeRegistry.All)
        {
            if (!catalog.PerMode.ContainsKey(mode.Slug))
                catalog.PerMode[mode.Slug] = LocalPerModeRuleSet.FromDefinition(mode);
        }
    }
}
