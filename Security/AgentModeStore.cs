using System.Text.Json;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Security;

internal sealed class AgentModeStore
{
    private static string SettingsPath => SecureDataPaths.PathFor("agent_mode.json");

    // Records that the agent has been set up at least once. Once present, a missing or
    // tampered mode file is treated as a bypass attempt (delete-to-get-the-default)
    // rather than a fresh install, so we fail closed instead of relaxing restrictions.
    private static string ConfiguredMarkerPath => SecureDataPaths.PathFor(".mode_configured");

    public StoredAgentMode Load()
    {
        var status = StateProtection.TryRead(SettingsPath, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredAgentMode>(json);
                if (stored is not null)
                {
                    MarkConfigured();
                    return stored;
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
            AuditLog.Write("SECURITY: agent_mode state failed integrity check — failing closed to Restricted Sub.");
            return StoredAgentMode.MostRestrictive;
        }

        // Missing.
        if (HasBeenConfigured())
        {
            AuditLog.Write("SECURITY: agent_mode state missing after setup — failing closed to Restricted Sub.");
            return StoredAgentMode.MostRestrictive;
        }

        return StoredAgentMode.Default;
    }

    public void Save(StoredAgentMode stored)
    {
        StateProtection.Write(SettingsPath, JsonSerializer.Serialize(stored));
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

    internal sealed class StoredAgentMode
    {
        public string Slug { get; init; } = AgentModeSlugs.TrustedSub;
        public string? DisplayName { get; init; }
        public int ScreenTimeLimitMinutes { get; init; } = AgentModeRegistry.TrustedSub.Defaults.ScreenTimeLimitMinutes;
        public Dictionary<string, int>? ScreenTimeWeeklyLimits { get; init; }
        public ModeFeatures Features { get; init; } = ModeFeatures.ForTrustedSub;

        public static StoredAgentMode Default => new();

        /// <summary>Safe posture applied when stored state is missing-after-setup or tampered.</summary>
        public static StoredAgentMode MostRestrictive => new()
        {
            Slug = AgentModeSlugs.RestrictedSub,
            ScreenTimeLimitMinutes = AgentModeRegistry.RestrictedSub.Defaults.ScreenTimeLimitMinutes,
            Features = ModeFeatures.ForRestrictedSub,
        };
    }
}
