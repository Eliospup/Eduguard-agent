using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Models;

internal sealed class RegisterRequest
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("os_info")]
    public required OsInfo OsInfo { get; init; }
}

internal sealed class OsInfo
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }
}

internal sealed class RegisterResponse
{
    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }

    [JsonPropertyName("agent_token")]
    public required string AgentToken { get; init; }
}

internal sealed class HeartbeatRequest
{
    [JsonPropertyName("focused_window")]
    public required string FocusedWindow { get; init; }

    [JsonPropertyName("running_apps")]
    public required IReadOnlyList<string> RunningApps { get; init; }

    [JsonPropertyName("is_idle")]
    public required bool IsIdle { get; init; }

    [JsonPropertyName("level")]
    public string? Level { get; init; }

    [JsonPropertyName("exit_pin_audit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExitPinAuditPayload? ExitPinAudit { get; set; }

    [JsonPropertyName("gaming_usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GamingUsagePayload? GamingUsage { get; set; }

    [JsonPropertyName("youtube_usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public YoutubeUsagePayload? YoutubeUsage { get; set; }

    [JsonPropertyName("punishment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PunishmentStatePayload? Punishment { get; set; }

    [JsonPropertyName("image_shield")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageShieldAgentStatusPayload? ImageShield { get; set; }
}

internal sealed class ExitPinAuditPayload
{
    [JsonPropertyName("successes")]
    public int Successes { get; init; }

    [JsonPropertyName("failures")]
    public int Failures { get; init; }
}

internal sealed class HeartbeatResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("server_time")]
    public string? ServerTime { get; init; }

    [JsonPropertyName("settings")]
    public AgentSettingsPayload? Settings { get; init; }
}

internal sealed class AgentSettingsPayload
{
    [JsonPropertyName("bedtime")]
    public BedtimeSettingsPayload? Bedtime { get; init; }

    [JsonPropertyName("exit_pin")]
    public JsonElement? ExitPinElement { get; init; }

    [JsonPropertyName("gaming")]
    public GamingSettingsPayload? Gaming { get; init; }

    [JsonPropertyName("youtube")]
    public YoutubeSettingsPayload? Youtube { get; init; }

    [JsonPropertyName("study_time")]
    public StudyTimeSettingsPayload? StudyTime { get; init; }

    [JsonPropertyName("mode")]
    public ModeSettingsPayload? Mode { get; init; }

    [JsonPropertyName("screen_time")]
    public ScreenTimeSettingsPayload? ScreenTime { get; init; }

    [JsonPropertyName("kiosk")]
    public KioskSettingsPayload? Kiosk { get; init; }

    [JsonPropertyName("punishment")]
    public PunishmentSettingsPayload? Punishment { get; init; }

    [JsonPropertyName("image_shield")]
    public ImageShieldSettingsPayload? ImageShield { get; init; }

    public bool TryGetExitPin(out string? pin)
    {
        pin = null;
        if (ExitPinElement is not { } element)
            return false;

        pin = element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => null,
        };

        return element.ValueKind is JsonValueKind.Null or JsonValueKind.String;
    }
}

internal sealed class BedtimeSettingsPayload
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("time")]
    public string? Time { get; init; }

    [JsonPropertyName("wake_time")]
    public string? WakeTime { get; init; }

    [JsonPropertyName("blue_light_filter_enabled")]
    public bool? BlueLightFilterEnabled { get; init; }

    /// <summary>Per-day overrides keyed by sun–sat. Omitted days inherit top-level values.</summary>
    [JsonPropertyName("weekly")]
    public Dictionary<string, BedtimeDayPayload>? Weekly { get; init; }
}

internal sealed class ImageShieldSettingsPayload
{
    /// <summary>Master switch. When false the agent removes the extension force-install.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>Per supervision mode toggles (trusted_sub, sub, restricted_sub).</summary>
    [JsonPropertyName("per_mode")]
    public Dictionary<string, ImageShieldTogglePayload>? PerMode { get; init; }

    /// <summary>Per browser toggles (firefox, chrome, edge, brave).</summary>
    [JsonPropertyName("per_browser")]
    public Dictionary<string, ImageShieldTogglePayload>? PerBrowser { get; init; }

    /// <summary>Minimum image side (px) before classification. Default 64.</summary>
    [JsonPropertyName("min_size")]
    public int? MinSize { get; init; }

    /// <summary>Block score (0..1) above which the blur is kept. Default 0.6.</summary>
    [JsonPropertyName("nsfw_threshold")]
    public double? NsfwThreshold { get; init; }

    /// <summary>Weight of the softer "Sexy" class (0..1). Default 0.5.</summary>
    [JsonPropertyName("sexy_weight")]
    public double? SexyWeight { get; init; }

    /// <summary>Classification rate cap per second. Default 12.</summary>
    [JsonPropertyName("max_per_second")]
    public int? MaxPerSecond { get; init; }
}

internal sealed class ImageShieldTogglePayload
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

internal sealed class ImageShieldAgentStatusPayload
{
    [JsonPropertyName("global_enabled")]
    public bool GlobalEnabled { get; init; }

    [JsonPropertyName("effective_enabled")]
    public bool EffectiveEnabled { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("configured")]
    public bool Configured { get; init; }

    [JsonPropertyName("policies_active")]
    public bool PoliciesActive { get; init; }

    [JsonPropertyName("has_server_config")]
    public bool HasServerConfig { get; init; }

    [JsonPropertyName("browsers")]
    public Dictionary<string, ImageShieldBrowserStatusPayload> Browsers { get; init; } = new();
}

internal sealed class ImageShieldBrowserStatusPayload
{
    [JsonPropertyName("enabled_by_dom")]
    public bool EnabledByDom { get; init; }

    [JsonPropertyName("available")]
    public bool Available { get; init; }

    [JsonPropertyName("enforced")]
    public bool Enforced { get; init; }

    [JsonPropertyName("unavailable_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnavailableReason { get; init; }

    [JsonPropertyName("requires_dev_edition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequiresDevEdition { get; init; }
}

internal sealed class GamingSettingsPayload
{
    [JsonPropertyName("daily_limit_minutes")]
    public int? DailyLimitMinutes { get; init; }

    /// <summary>Per-day minute caps keyed by sun–sat. Omitted days use <see cref="DailyLimitMinutes"/>.</summary>
    [JsonPropertyName("weekly_limits")]
    public Dictionary<string, int>? WeeklyLimits { get; init; }

    [JsonPropertyName("extra_games")]
    public List<GamingExtraGamePayload>? ExtraGames { get; init; }

    [JsonPropertyName("ignored_games")]
    public List<string>? IgnoredGames { get; init; }

    [JsonPropertyName("show_playtime_overlay")]
    public bool? ShowPlaytimeOverlay { get; init; }

    /// <summary>Per-game daily caps (exe → minutes). Omitted games only share the global pool.</summary>
    [JsonPropertyName("game_limits")]
    public Dictionary<string, int>? GameLimits { get; init; }
}

internal sealed class GamingExtraGamePayload
{
    [JsonPropertyName("exe")]
    public required string Exe { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

internal sealed class KioskSettingsPayload
{
    [JsonPropertyName("approved_apps")]
    public List<KioskAppPayload>? ApprovedApps { get; init; }
}

internal sealed class KioskAppPayload
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("args")]
    public string? Args { get; init; }

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
}

internal sealed class GamingUsagePayload
{
    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("total_seconds")]
    public int TotalSeconds { get; init; }

    [JsonPropertyName("limit_minutes")]
    public int LimitMinutes { get; init; }

    [JsonPropertyName("games")]
    public List<GamingUsageGamePayload> Games { get; init; } = [];
}

internal sealed class GamingUsageGamePayload
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("seconds")]
    public int Seconds { get; init; }

    [JsonPropertyName("limit_minutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LimitMinutes { get; init; }
}

internal sealed class YoutubeSettingsPayload
{
    [JsonPropertyName("daily_limit_minutes")]
    public int? DailyLimitMinutes { get; init; }

    [JsonPropertyName("weekly_limits")]
    public Dictionary<string, int>? WeeklyLimits { get; init; }

    [JsonPropertyName("show_overlay")]
    public bool? ShowOverlay { get; init; }

    /// <summary>When true, locks YouTube restricted mode (strict) in Chromium browsers via policy.</summary>
    [JsonPropertyName("restricted_mode_enabled")]
    public bool? RestrictedModeEnabled { get; init; }
}

internal sealed class YoutubeUsagePayload
{
    [JsonPropertyName("date")]
    public required string Date { get; init; }

    [JsonPropertyName("total_seconds")]
    public int TotalSeconds { get; init; }

    [JsonPropertyName("limit_minutes")]
    public int LimitMinutes { get; init; }
}

internal sealed class CommandsResponse
{
    [JsonPropertyName("commands")]
    public List<AgentCommand> Commands { get; init; } = [];
}

internal sealed class AgentCommand
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("payload")]
    public Dictionary<string, JsonElement>? Payload { get; init; }

    [JsonPropertyName("issued_at")]
    public string? IssuedAt { get; init; }
}

internal sealed class CommandResultRequest
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("result")]
    public required object Result { get; init; }
}

internal sealed class UploadResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("upload_id")]
    public string? UploadId { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed class CapabilitiesResponse
{
    [JsonPropertyName("protocol_version")]
    public int ProtocolVersion { get; init; }

    [JsonPropertyName("commands")]
    public List<CommandCapability> Commands { get; init; } = [];
}

internal sealed class CommandCapability
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonPropertyName("fields")]
    public List<CommandCapabilityField>? Fields { get; init; }
}

internal sealed class CommandCapabilityField
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
