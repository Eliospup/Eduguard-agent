namespace EduGuardAgent.Models;

using EduGuardAgent.Profiles;

internal static class AgentModeSlugs
{
    public const string TrustedSub = "trusted_sub";
    public const string Sub = "sub";
    public const string RestrictedSub = "restricted_sub";

    public static bool IsKnown(string? slug) =>
        slug is TrustedSub or Sub or RestrictedSub;

    public static string Normalize(string? slug) =>
        IsKnown(slug) ? slug! : Sub;
}

internal sealed class ModeFeatures
{
    public bool BlockTaskManager { get; init; }
    public bool VpnShield { get; init; } = true;
    public bool BlockRegistryEditor { get; init; }
    public bool BlockCommandPrompt { get; init; }
    public bool BlockPowerShell { get; init; }
    public bool BlockSystemConfig { get; init; }
    public bool BlockControlPanel { get; init; }
    public bool BlockProcessTools { get; init; }
    public bool BlockProcessKillers { get; init; } = true;
    public bool KioskMode { get; init; }

    public bool HasSystemToolLock =>
        BlockRegistryEditor
        || BlockCommandPrompt
        || BlockPowerShell
        || BlockSystemConfig
        || BlockControlPanel
        || BlockProcessTools;

    public static ModeFeatures ForTrustedSub => new()
    {
        BlockTaskManager = false,
        VpnShield = true,
        BlockProcessKillers = false,
    };

    public static ModeFeatures ForSub => new()
    {
        BlockTaskManager = true,
        VpnShield = true,
        BlockProcessKillers = true,
    };

    public static ModeFeatures ForRestrictedSub => new()
    {
        BlockTaskManager = true,
        VpnShield = true,
        BlockRegistryEditor = true,
        BlockCommandPrompt = true,
        BlockPowerShell = true,
        BlockSystemConfig = true,
        BlockProcessTools = true,
        BlockProcessKillers = true,
        KioskMode = true,
    };
}

internal sealed class ModeRuleDefaults
{
    public int ScreenTimeLimitMinutes { get; init; }
    public int GamingTimeLimitMinutes { get; init; }
    public int YoutubeTimeLimitMinutes { get; init; }
    public bool BedtimeEnabled { get; init; }
    public string BedtimeTime { get; init; } = "23:00";
    public string WakeTime { get; init; } = "07:00";
    public bool StudyTimeEnabled { get; init; }
}

internal sealed class AgentModeDefinition
{
    public required string Slug { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortLabel { get; init; }
    public required Profiles.ModeCopySet Copy { get; init; }
    public required Profiles.ModeTheme Theme { get; init; }
    public required ModeRuleDefaults Defaults { get; init; }
    public required ModeFeatures Features { get; init; }
    public required ModeUiPresentation Ui { get; init; }
    public string ModeSubtitle { get; init; } = string.Empty;
}

internal sealed class ModeSettingsPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("features")]
    public ModeFeaturesPayload? Features { get; init; }
}

internal sealed class ModeFeaturesPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("block_task_manager")]
    public bool? BlockTaskManager { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("vpn_shield")]
    public bool? VpnShield { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_registry_editor")]
    public bool? BlockRegistryEditor { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_command_prompt")]
    public bool? BlockCommandPrompt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_powershell")]
    public bool? BlockPowerShell { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_system_config")]
    public bool? BlockSystemConfig { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_control_panel")]
    public bool? BlockControlPanel { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_process_tools")]
    public bool? BlockProcessTools { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("block_process_killers")]
    public bool? BlockProcessKillers { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("kiosk_mode")]
    public bool? KioskMode { get; init; }
}

internal sealed class ScreenTimeSettingsPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("daily_limit_minutes")]
    public int? DailyLimitMinutes { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("weekly_limits")]
    public Dictionary<string, int>? WeeklyLimits { get; init; }
}
