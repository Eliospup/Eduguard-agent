using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

/// <summary>Persists the punishment state machine (floor level, infraction count, decay timer) and Dom config.</summary>
internal sealed class PunishmentStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "punishment.json");

    public StoredPunishment Load()
    {
        if (!File.Exists(SettingsPath))
            return StoredPunishment.Default;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StoredPunishment>(json) ?? StoredPunishment.Default;
        }
        catch
        {
            return StoredPunishment.Default;
        }
    }

    public void Save(StoredPunishment stored)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored));
        }
        catch
        {
            // Best-effort persistence; never let it crash the agent.
        }
    }

    internal sealed class StoredInfractionRecord
    {
        public string Kind { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string At { get; set; } = string.Empty;
        public int TrustPointsLost { get; set; }
    }

    internal sealed class StoredPunishment
    {
        public int FloorIndex { get; set; }
        public int InfractionCount { get; set; }
        public DateTimeOffset? PunishmentUntil { get; set; }

        /// <summary>Trust gauge (0-100). Defaults to full so existing installs start trusted.</summary>
        public double Trust { get; set; } = PunishmentSettings.MaxTrust;

        public int RegenPerHour { get; set; } = 5;

        public int WeightVpn { get; set; } = 25;
        public int WeightBypass { get; set; } = 20;
        public int WeightBlockedApp { get; set; } = 15;
        public int WeightBlockedSearch { get; set; } = 15;
        public int WeightStudy { get; set; } = 10;
        public int WeightLimit { get; set; } = 10;

        public bool Enabled { get; set; } = true;

        public int ThresholdTrustedToSub { get; set; }
        public int ThresholdSubToRestricted { get; set; }
        public int InfractionThreshold { get; set; } = 3;

        public int EscalationHours { get; set; } = 6;
        public int EscalationMinutes { get; set; }
        public double InfractionExtensionHours { get; set; } = 2;

        public int ExtensionVpnHours { get; set; }
        public int ExtensionVpnMinutes { get; set; } = 30;
        public int ExtensionBlockedAppHours { get; set; }
        public int ExtensionBlockedAppMinutes { get; set; } = 30;
        public int ExtensionBypassHours { get; set; }
        public int ExtensionBypassMinutes { get; set; } = 30;
        public int ExtensionLimitHours { get; set; }
        public int ExtensionLimitMinutes { get; set; } = 30;
        public int ExtensionStudyHours { get; set; }
        public int ExtensionStudyMinutes { get; set; } = 30;
        public int ExtensionBlockedSearchHours { get; set; }
        public int ExtensionBlockedSearchMinutes { get; set; } = 30;

        public bool InfractionVpnAttempt { get; set; } = true;
        public bool InfractionBlockedAppRepeated { get; set; } = true;
        public bool InfractionBypassAttempt { get; set; } = true;
        public bool InfractionLimitIgnored { get; set; } = true;
        public bool InfractionStudyTimeViolation { get; set; } = true;
        public bool InfractionBlockedSearch { get; set; } = true;

        public List<StoredInfractionRecord>? RecentInfractions { get; set; }

        public static StoredPunishment Default => new();

        public PunishmentSettings ToSettings()
        {
            var legacyThreshold = InfractionThreshold > 0 ? InfractionThreshold : 3;
            var trustedThreshold = ThresholdTrustedToSub > 0 ? ThresholdTrustedToSub : legacyThreshold;
            var restrictedThreshold = ThresholdSubToRestricted > 0 ? ThresholdSubToRestricted : legacyThreshold;
            var legacyExtension = InfractionExtensionHours > 0
                ? DurationParts.FromTotalMinutes((int)Math.Round(InfractionExtensionHours * 60))
                : DurationParts.DefaultExtension;

            return new PunishmentSettings
            {
                Enabled = Enabled,
                RegenPerHour = RegenPerHour > 0 ? RegenPerHour : 5,
                InfractionWeights = new InfractionWeightSettings
                {
                    VpnAttempt = WeightVpn > 0 ? WeightVpn : 25,
                    BypassAttempt = WeightBypass > 0 ? WeightBypass : 20,
                    BlockedAppRepeated = WeightBlockedApp > 0 ? WeightBlockedApp : 15,
                    BlockedSearch = WeightBlockedSearch > 0 ? WeightBlockedSearch : 15,
                    StudyTimeViolation = WeightStudy > 0 ? WeightStudy : 10,
                    LimitIgnored = WeightLimit > 0 ? WeightLimit : 10,
                },
                ThresholdTrustedToSub = trustedThreshold,
                ThresholdSubToRestricted = restrictedThreshold,
                EscalationHours = EscalationHours > 0 ? EscalationHours : 6,
                EscalationMinutes = EscalationMinutes,
                InfractionKinds = new InfractionKindSettings
                {
                    VpnAttempt = InfractionVpnAttempt,
                    BlockedAppRepeated = InfractionBlockedAppRepeated,
                    BypassAttempt = InfractionBypassAttempt,
                    LimitIgnored = InfractionLimitIgnored,
                    StudyTimeViolation = InfractionStudyTimeViolation,
                    BlockedSearch = InfractionBlockedSearch,
                },
                InfractionExtensions = new InfractionExtensionSettings
                {
                    VpnAttempt = ReadExtension(ExtensionVpnHours, ExtensionVpnMinutes, legacyExtension),
                    BlockedAppRepeated = ReadExtension(ExtensionBlockedAppHours, ExtensionBlockedAppMinutes, legacyExtension),
                    BypassAttempt = ReadExtension(ExtensionBypassHours, ExtensionBypassMinutes, legacyExtension),
                    LimitIgnored = ReadExtension(ExtensionLimitHours, ExtensionLimitMinutes, legacyExtension),
                    StudyTimeViolation = ReadExtension(ExtensionStudyHours, ExtensionStudyMinutes, legacyExtension),
                    BlockedSearch = ReadExtension(ExtensionBlockedSearchHours, ExtensionBlockedSearchMinutes, legacyExtension),
                },
            }.Sanitized();
        }

        private static DurationParts ReadExtension(int hours, int minutes, DurationParts legacy)
        {
            if (hours > 0 || minutes > 0)
                return new DurationParts(hours, minutes);

            return legacy;
        }
    }
}
