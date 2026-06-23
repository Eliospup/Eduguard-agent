using System.Text.Json.Serialization;

namespace EduGuardAgent.Models;

/// <summary>The kinds of behaviour that count as an infraction toward auto-escalation.</summary>
internal enum InfractionKind
{
    VpnAttempt,
    BlockedAppRepeated,
    BypassAttempt,
    LimitIgnored,
    StudyTimeViolation,
    BlockedSearch,
}

/// <summary>Which behaviours count toward auto-escalation. All default to enabled.</summary>
internal sealed class InfractionKindSettings
{
    public bool VpnAttempt { get; init; } = true;
    public bool BlockedAppRepeated { get; init; } = true;
    public bool BypassAttempt { get; init; } = true;
    public bool LimitIgnored { get; init; } = true;
    public bool StudyTimeViolation { get; init; } = true;
    public bool BlockedSearch { get; init; } = true;

    public static InfractionKindSettings Default => new();

    public bool IsEnabled(InfractionKind kind) => kind switch
    {
        InfractionKind.VpnAttempt => VpnAttempt,
        InfractionKind.BlockedAppRepeated => BlockedAppRepeated,
        InfractionKind.BypassAttempt => BypassAttempt,
        InfractionKind.LimitIgnored => LimitIgnored,
        InfractionKind.StudyTimeViolation => StudyTimeViolation,
        InfractionKind.BlockedSearch => BlockedSearch,
        _ => false,
    };

    public InfractionKindSettings Merge(InfractionKindsPayload? payload) =>
        payload is null
            ? this
            : new InfractionKindSettings
            {
                VpnAttempt = payload.VpnAttempt ?? VpnAttempt,
                BlockedAppRepeated = payload.BlockedAppRepeated ?? BlockedAppRepeated,
                BypassAttempt = payload.BypassAttempt ?? BypassAttempt,
                LimitIgnored = payload.LimitIgnored ?? LimitIgnored,
                StudyTimeViolation = payload.StudyTimeViolation ?? StudyTimeViolation,
                BlockedSearch = payload.BlockedSearch ?? BlockedSearch,
            };

    public InfractionKindsPayload ToPayload() => new()
    {
        VpnAttempt = VpnAttempt,
        BlockedAppRepeated = BlockedAppRepeated,
        BypassAttempt = BypassAttempt,
        LimitIgnored = LimitIgnored,
        StudyTimeViolation = StudyTimeViolation,
        BlockedSearch = BlockedSearch,
    };
}

/// <summary>Hours + minutes pair for punishment durations.</summary>
internal readonly record struct DurationParts(int Hours, int Minutes)
{
    public static DurationParts DefaultExtension => new(0, 30);

    public static DurationParts FromTotalMinutes(int totalMinutes)
    {
        totalMinutes = Math.Max(0, totalMinutes);
        return new(totalMinutes / 60, totalMinutes % 60);
    }

    public int TotalMinutes => Math.Max(0, Hours * 60 + Minutes);

    public DurationParts Sanitized() => new(
        Math.Clamp(Hours, 0, 720),
        Math.Clamp(Minutes, 0, 59));

    public TimeSpan ToTimeSpan(bool testingShortPunishment)
    {
        var sanitized = Sanitized();
        if (testingShortPunishment)
            return TimeSpan.FromMinutes(Math.Max(1, sanitized.TotalMinutes));

        return TimeSpan.FromHours(sanitized.Hours).Add(TimeSpan.FromMinutes(sanitized.Minutes));
    }
}

/// <summary>Per-kind extra time added when an infraction does not trigger escalation.</summary>
internal sealed class InfractionExtensionSettings
{
    public DurationParts VpnAttempt { get; init; } = DurationParts.DefaultExtension;
    public DurationParts BlockedAppRepeated { get; init; } = DurationParts.DefaultExtension;
    public DurationParts BypassAttempt { get; init; } = DurationParts.DefaultExtension;
    public DurationParts LimitIgnored { get; init; } = DurationParts.DefaultExtension;
    public DurationParts StudyTimeViolation { get; init; } = DurationParts.DefaultExtension;
    public DurationParts BlockedSearch { get; init; } = DurationParts.DefaultExtension;

    public static InfractionExtensionSettings Default => new();

    public DurationParts For(InfractionKind kind) => kind switch
    {
        InfractionKind.VpnAttempt => VpnAttempt,
        InfractionKind.BlockedAppRepeated => BlockedAppRepeated,
        InfractionKind.BypassAttempt => BypassAttempt,
        InfractionKind.LimitIgnored => LimitIgnored,
        InfractionKind.StudyTimeViolation => StudyTimeViolation,
        InfractionKind.BlockedSearch => BlockedSearch,
        _ => DurationParts.DefaultExtension,
    };

    public InfractionExtensionSettings Merge(InfractionExtensionsPayload? payload) =>
        payload is null
            ? this
            : new InfractionExtensionSettings
            {
                VpnAttempt = MergePart(VpnAttempt, payload.VpnAttempt),
                BlockedAppRepeated = MergePart(BlockedAppRepeated, payload.BlockedAppRepeated),
                BypassAttempt = MergePart(BypassAttempt, payload.BypassAttempt),
                LimitIgnored = MergePart(LimitIgnored, payload.LimitIgnored),
                StudyTimeViolation = MergePart(StudyTimeViolation, payload.StudyTimeViolation),
                BlockedSearch = MergePart(BlockedSearch, payload.BlockedSearch),
            };

    public InfractionExtensionsPayload ToPayload() => new()
    {
        VpnAttempt = VpnAttempt.ToPayload(),
        BlockedAppRepeated = BlockedAppRepeated.ToPayload(),
        BypassAttempt = BypassAttempt.ToPayload(),
        LimitIgnored = LimitIgnored.ToPayload(),
        StudyTimeViolation = StudyTimeViolation.ToPayload(),
        BlockedSearch = BlockedSearch.ToPayload(),
    };

    private static DurationParts MergePart(DurationParts current, DurationPartsPayload? payload) =>
        payload is null
            ? current
            : new DurationParts(
                payload.Hours ?? current.Hours,
                payload.Minutes ?? current.Minutes).Sanitized();
}

internal static class DurationPartsPayloadExtensions
{
    public static DurationPartsPayload ToPayload(this DurationParts parts) => new()
    {
        Hours = parts.Hours,
        Minutes = parts.Minutes,
    };
}

/// <summary>
/// Dom-configurable parameters for the auto-escalation punishment system.
/// </summary>
internal sealed class PunishmentSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Infractions required to escalate Trusted Sub → Sub.</summary>
    public int ThresholdTrustedToSub { get; init; } = 3;

    /// <summary>Infractions required to escalate Sub → Restricted Sub (or extend at max).</summary>
    public int ThresholdSubToRestricted { get; init; } = 3;

    /// <summary>Fixed time added when an escalation threshold is reached.</summary>
    public int EscalationHours { get; init; } = 6;

    public int EscalationMinutes { get; init; }

    public InfractionKindSettings InfractionKinds { get; init; } = InfractionKindSettings.Default;

    public InfractionExtensionSettings InfractionExtensions { get; init; } = InfractionExtensionSettings.Default;

    public static PunishmentSettings Default => new();

    public int ThresholdForFloor(int floorIndex) =>
        floorIndex <= 0 ? ThresholdTrustedToSub : ThresholdSubToRestricted;

    public PunishmentSettings Sanitized() => new()
    {
        Enabled = Enabled,
        ThresholdTrustedToSub = Math.Clamp(ThresholdTrustedToSub, 1, 50),
        ThresholdSubToRestricted = Math.Clamp(ThresholdSubToRestricted, 1, 50),
        EscalationHours = Math.Clamp(EscalationHours, 0, 720),
        EscalationMinutes = Math.Clamp(EscalationMinutes, 0, 59),
        InfractionKinds = InfractionKinds,
        InfractionExtensions = InfractionExtensions,
    };
}

internal sealed class InfractionKindsPayload
{
    [JsonPropertyName("vpn_attempt")]
    public bool? VpnAttempt { get; init; }

    [JsonPropertyName("blocked_app_repeated")]
    public bool? BlockedAppRepeated { get; init; }

    [JsonPropertyName("bypass_attempt")]
    public bool? BypassAttempt { get; init; }

    [JsonPropertyName("limit_ignored")]
    public bool? LimitIgnored { get; init; }

    [JsonPropertyName("study_time_violation")]
    public bool? StudyTimeViolation { get; init; }

    [JsonPropertyName("blocked_search")]
    public bool? BlockedSearch { get; init; }
}

internal sealed class DurationPartsPayload
{
    [JsonPropertyName("hours")]
    public int? Hours { get; init; }

    [JsonPropertyName("minutes")]
    public int? Minutes { get; init; }
}

internal sealed class InfractionExtensionsPayload
{
    [JsonPropertyName("vpn_attempt")]
    public DurationPartsPayload? VpnAttempt { get; init; }

    [JsonPropertyName("blocked_app_repeated")]
    public DurationPartsPayload? BlockedAppRepeated { get; init; }

    [JsonPropertyName("bypass_attempt")]
    public DurationPartsPayload? BypassAttempt { get; init; }

    [JsonPropertyName("limit_ignored")]
    public DurationPartsPayload? LimitIgnored { get; init; }

    [JsonPropertyName("study_time_violation")]
    public DurationPartsPayload? StudyTimeViolation { get; init; }

    [JsonPropertyName("blocked_search")]
    public DurationPartsPayload? BlockedSearch { get; init; }
}

internal sealed class PunishmentSettingsPayload
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("threshold_trusted_to_sub")]
    public int? ThresholdTrustedToSub { get; init; }

    [JsonPropertyName("threshold_sub_to_restricted")]
    public int? ThresholdSubToRestricted { get; init; }

    /// <summary>Legacy — applies to both thresholds when the new fields are omitted.</summary>
    [JsonPropertyName("infraction_threshold")]
    public int? InfractionThreshold { get; init; }

    [JsonPropertyName("escalation_hours")]
    public int? EscalationHours { get; init; }

    [JsonPropertyName("escalation_minutes")]
    public int? EscalationMinutes { get; init; }

    /// <summary>Legacy — converted to 0h + N minutes for every infraction kind.</summary>
    [JsonPropertyName("infraction_extension_hours")]
    public double? InfractionExtensionHours { get; init; }

    [JsonPropertyName("infraction_extensions")]
    public InfractionExtensionsPayload? InfractionExtensions { get; init; }

    [JsonPropertyName("infraction_kinds")]
    public InfractionKindsPayload? InfractionKinds { get; init; }
}

internal static class InfractionKindExtensions
{
    public static string ToApiKey(this InfractionKind kind) => kind switch
    {
        InfractionKind.VpnAttempt => "vpn_attempt",
        InfractionKind.BlockedAppRepeated => "blocked_app_repeated",
        InfractionKind.BypassAttempt => "bypass_attempt",
        InfractionKind.LimitIgnored => "limit_ignored",
        InfractionKind.StudyTimeViolation => "study_time_violation",
        InfractionKind.BlockedSearch => "blocked_search",
        _ => "unknown",
    };
}

/// <summary>Telemetry reported in heartbeat requests (runtime state only — no config mirrors).</summary>
internal sealed class PunishmentStatePayload
{
    [JsonPropertyName("base_level")]
    public string? BaseLevel { get; init; }

    [JsonPropertyName("effective_level")]
    public string? EffectiveLevel { get; init; }

    [JsonPropertyName("floor_index")]
    public int FloorIndex { get; init; }

    [JsonPropertyName("is_punished")]
    public bool IsPunished { get; init; }

    [JsonPropertyName("infraction_count")]
    public int InfractionCount { get; init; }

    [JsonPropertyName("punishment_until")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PunishmentUntil { get; init; }

    [JsonPropertyName("seconds_until_decay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SecondsUntilDecay { get; init; }

    [JsonPropertyName("recent_infractions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<InfractionEventPayload>? RecentInfractions { get; init; }
}

internal sealed class InfractionEventPayload
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }

    [JsonPropertyName("at")]
    public required string At { get; init; }
}
