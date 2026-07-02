using System.ComponentModel;
using System.Text.Json.Serialization;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Models;

/// <summary>Editable rule template for one supervision mode (mirrors web per-mode rules).</summary>
internal sealed class LocalPerModeRuleSet
{
    public int ScreenTimeDailyLimitMinutes { get; set; } = 300;
    public Dictionary<string, int>? ScreenTimeWeekly { get; set; }

    public int GamingDailyLimitMinutes { get; set; } = 60;
    public Dictionary<string, int>? GamingWeekly { get; set; }
    public bool GamingShowOverlay { get; set; } = true;

    /// <summary>Daily foreground time limits per app exe (local mode).</summary>
    [JsonPropertyName("gamingGameLimits")]
    public Dictionary<string, int>? AppTimeLimits { get; set; }

    public int YoutubeDailyLimitMinutes { get; set; } = 30;
    public Dictionary<string, int>? YoutubeWeekly { get; set; }
    public bool YoutubeShowOverlay { get; set; } = true;

    public bool BedtimeEnabled { get; set; } = true;
    public string BedtimeTime { get; set; } = "23:00";
    public string WakeTime { get; set; } = "07:00";
    public Dictionary<string, LocalBedtimeDayOverride>? BedtimeWeekly { get; set; }

    public bool StudyEnabled { get; set; }
    public string StudyStart { get; set; } = "09:00";
    public string StudyEnd { get; set; } = "17:00";
    public List<string> StudyDays { get; set; } = ["mon", "tue", "wed", "thu", "fri"];
    public Dictionary<string, LocalStudyDayOverride>? StudyWeekly { get; set; }
    public bool StudyBlockGames { get; set; } = true;
    public bool StudyBlockYoutube { get; set; } = true;
    public bool StudyBlockDistractingSites { get; set; } = true;
    public bool StudyBlockDistractingApps { get; set; } = true;

    public bool BlockTaskManager { get; set; }
    public bool VpnShield { get; set; } = true;
    public bool BlockRegistryEditor { get; set; }
    public bool BlockCommandPrompt { get; set; }
    public bool BlockPowerShell { get; set; }
    public bool BlockSystemConfig { get; set; }
    public bool BlockControlPanel { get; set; }
    public bool BlockProcessTools { get; set; }
    public bool BlockProcessKillers { get; set; } = true;
    public bool KioskMode { get; set; }

    public static LocalPerModeRuleSet FromDefinition(AgentModeDefinition def) => new()
    {
        ScreenTimeDailyLimitMinutes = def.Defaults.ScreenTimeLimitMinutes,
        GamingDailyLimitMinutes = def.Defaults.GamingTimeLimitMinutes,
        YoutubeDailyLimitMinutes = def.Defaults.YoutubeTimeLimitMinutes,
        BedtimeEnabled = def.Defaults.BedtimeEnabled,
        BedtimeTime = def.Defaults.BedtimeTime,
        WakeTime = def.Defaults.WakeTime,
        StudyEnabled = def.Defaults.StudyTimeEnabled,
        BlockTaskManager = def.Features.BlockTaskManager,
        VpnShield = def.Features.VpnShield,
        BlockRegistryEditor = def.Features.BlockRegistryEditor,
        BlockCommandPrompt = def.Features.BlockCommandPrompt,
        BlockPowerShell = def.Features.BlockPowerShell,
        BlockSystemConfig = def.Features.BlockSystemConfig,
        BlockControlPanel = def.Features.BlockControlPanel,
        BlockProcessTools = def.Features.BlockProcessTools,
        BlockProcessKillers = def.Features.BlockProcessKillers,
        KioskMode = def.Features.KioskMode,
    };

    public ModeFeatures ToFeatures() => new()
    {
        BlockTaskManager = BlockTaskManager,
        VpnShield = VpnShield,
        BlockRegistryEditor = BlockRegistryEditor,
        BlockCommandPrompt = BlockCommandPrompt,
        BlockPowerShell = BlockPowerShell,
        BlockSystemConfig = BlockSystemConfig,
        BlockControlPanel = BlockControlPanel,
        BlockProcessTools = BlockProcessTools,
        BlockProcessKillers = BlockProcessKillers,
        KioskMode = KioskMode,
    };

    public ModeFeaturesPayload ToFeaturesPayload() => new()
    {
        BlockTaskManager = BlockTaskManager,
        VpnShield = VpnShield,
        BlockRegistryEditor = BlockRegistryEditor,
        BlockCommandPrompt = BlockCommandPrompt,
        BlockPowerShell = BlockPowerShell,
        BlockSystemConfig = BlockSystemConfig,
        BlockControlPanel = BlockControlPanel,
        BlockProcessTools = BlockProcessTools,
        BlockProcessKillers = BlockProcessKillers,
        KioskMode = KioskMode,
    };

    public ScreenTimeSettingsPayload ToScreenTimePayload() => new()
    {
        DailyLimitMinutes = ScreenTimeDailyLimitMinutes,
        WeeklyLimits = ScreenTimeWeekly,
    };

    public GamingSettingsPayload ToGamingPayload() => new()
    {
        DailyLimitMinutes = GamingDailyLimitMinutes,
        WeeklyLimits = GamingWeekly,
        ShowPlaytimeOverlay = GamingShowOverlay,
    };

    public YoutubeSettingsPayload ToYoutubePayload() => new()
    {
        DailyLimitMinutes = YoutubeDailyLimitMinutes,
        WeeklyLimits = YoutubeWeekly,
        ShowOverlay = YoutubeShowOverlay,
    };

    public BedtimeSettingsPayload ToBedtimePayload() => new()
    {
        Enabled = BedtimeEnabled,
        Time = BedtimeTime,
        WakeTime = WakeTime,
        Weekly = BedtimeWeekly?.Count > 0
            ? BedtimeWeekly.ToDictionary(
                p => p.Key,
                p => new BedtimeDayPayload
                {
                    Enabled = p.Value.Enabled,
                    Time = p.Value.Time,
                    WakeTime = p.Value.WakeTime,
                },
                StringComparer.OrdinalIgnoreCase)
            : null,
    };

    public StudyTimeSettingsPayload ToStudyPayload() => new()
    {
        Enabled = StudyEnabled,
        StartTime = StudyStart,
        EndTime = StudyEnd,
        Days = StudyDays,
        Weekly = StudyWeekly?.Count > 0
            ? StudyWeekly.ToDictionary(
                p => p.Key,
                p => new StudyDayPayload
                {
                    Enabled = p.Value.Enabled,
                    StartTime = p.Value.StartTime,
                    EndTime = p.Value.EndTime,
                },
                StringComparer.OrdinalIgnoreCase)
            : null,
        BlockGames = StudyBlockGames,
        BlockYoutube = StudyBlockYoutube,
        BlockDistractingSites = StudyBlockDistractingSites,
        BlockDistractingApps = StudyBlockDistractingApps,
    };
}

internal sealed class LocalSettingsCatalog
{
    public string ActiveModeSlug { get; set; } = AgentModeSlugs.Sub;

    public Dictionary<string, LocalPerModeRuleSet> PerMode { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool ImageShieldEnabled { get; set; } = true;
    public bool YouTubeRestrictedModeEnabled { get; set; }
    public Dictionary<string, bool>? ImageShieldPerMode { get; set; }
    public Dictionary<string, bool>? ImageShieldPerBrowser { get; set; }
    public int? ImageShieldMinSize { get; set; }
    public double? ImageShieldNsfwThreshold { get; set; }
    public double? ImageShieldSexyWeight { get; set; }
    public int? ImageShieldMaxPerSecond { get; set; }

    public string? ExitPin { get; set; }

    public bool PunishmentEnabled { get; set; } = true;
    public int PunishmentThresholdTrustedToSub { get; set; } = 3;
    public int PunishmentThresholdSubToRestricted { get; set; } = 3;
    public int PunishmentInfractionThreshold { get; set; } = 3;
    public int PunishmentEscalationHours { get; set; } = 24;
    public int PunishmentEscalationMinutes { get; set; }
    public PunishmentExtensionCatalog PunishmentExtensions { get; set; } = PunishmentExtensionCatalog.CreateDefaults();

    /// <summary>Trust points regained per hour of clean, supervised time.</summary>
    public int TrustRegenPerHour { get; set; } = 5;

    public int TrustWeightVpn { get; set; } = 25;
    public int TrustWeightBypass { get; set; } = 20;
    public int TrustWeightBlockedApp { get; set; } = 15;
    public int TrustWeightBlockedSearch { get; set; } = 15;
    public int TrustWeightStudy { get; set; } = 10;
    public int TrustWeightLimit { get; set; } = 10;

    public bool InfractionVpnAttempt { get; set; } = true;
    public bool InfractionBlockedAppRepeated { get; set; } = true;
    public bool InfractionBypassAttempt { get; set; } = true;
    public bool InfractionLimitIgnored { get; set; } = true;
    public bool InfractionStudyTimeViolation { get; set; } = true;
    public bool InfractionBlockedSearch { get; set; } = true;

    public bool BlueLightFilterEnabled { get; set; } = true;
    public bool DesktopWidgetRemindersEnabled { get; set; } = true;
    public int DesktopWidgetReminderFrequencyMinutes { get; set; } = 15;

    // --- Appearance / on-screen visuals (global, all modes) ----------------
    // Master switches to hide Guardi's visible surfaces without changing what is
    // actually enforced. Timers still tick, sites are still filtered — only the UI
    // chrome is hidden. newtab + website badge live in the extension and are pushed
    // via shield-state (take effect after the extension picks up the state).
    public bool ShowDesktopWidget { get; set; } = true;
    public bool ShowGamingTimer { get; set; } = true;
    public bool ShowYoutubeTimer { get; set; } = true;
    public bool StyledNewTabPage { get; set; } = true;
    public bool ShowWebsiteBadge { get; set; } = true;

    public int ScreenshotIntervalMinutes { get; set; } = Config.ScreenshotIntervalMinutes;

    public List<string> BlockedApps { get; set; } = [];
    public List<string> BlockedHosts { get; set; } = [];

    public static LocalSettingsCatalog CreateDefaults()
    {
        var catalog = new LocalSettingsCatalog();
        foreach (var mode in AgentModeRegistry.All)
            catalog.PerMode[mode.Slug] = LocalPerModeRuleSet.FromDefinition(mode);
        return catalog;
    }
}

internal sealed class PunishmentExtensionCatalog
{
    public int VpnHours { get; set; }
    public int VpnMinutes { get; set; } = 30;
    public int BlockedAppHours { get; set; }
    public int BlockedAppMinutes { get; set; } = 30;
    public int BypassHours { get; set; }
    public int BypassMinutes { get; set; } = 30;
    public int LimitHours { get; set; }
    public int LimitMinutes { get; set; } = 30;
    public int StudyHours { get; set; }
    public int StudyMinutes { get; set; } = 30;
    public int BlockedSearchHours { get; set; }
    public int BlockedSearchMinutes { get; set; } = 30;

    public static PunishmentExtensionCatalog CreateDefaults() => new();

    public InfractionExtensionSettings ToSettings() => new()
    {
        VpnAttempt = new DurationParts(VpnHours, VpnMinutes),
        BlockedAppRepeated = new DurationParts(BlockedAppHours, BlockedAppMinutes),
        BypassAttempt = new DurationParts(BypassHours, BypassMinutes),
        LimitIgnored = new DurationParts(LimitHours, LimitMinutes),
        StudyTimeViolation = new DurationParts(StudyHours, StudyMinutes),
        BlockedSearch = new DurationParts(BlockedSearchHours, BlockedSearchMinutes),
    };

    public static PunishmentExtensionCatalog FromSettings(InfractionExtensionSettings settings) => new()
    {
        VpnHours = settings.VpnAttempt.Hours,
        VpnMinutes = settings.VpnAttempt.Minutes,
        BlockedAppHours = settings.BlockedAppRepeated.Hours,
        BlockedAppMinutes = settings.BlockedAppRepeated.Minutes,
        BypassHours = settings.BypassAttempt.Hours,
        BypassMinutes = settings.BypassAttempt.Minutes,
        LimitHours = settings.LimitIgnored.Hours,
        LimitMinutes = settings.LimitIgnored.Minutes,
        StudyHours = settings.StudyTimeViolation.Hours,
        StudyMinutes = settings.StudyTimeViolation.Minutes,
        BlockedSearchHours = settings.BlockedSearch.Hours,
        BlockedSearchMinutes = settings.BlockedSearch.Minutes,
    };
}

internal sealed class LocalAppTimeLimitItem : INotifyPropertyChanged
{
    private string _exe = string.Empty;
    private int _limitMinutes = 30;

    public string Exe
    {
        get => _exe;
        set
        {
            if (_exe == value)
                return;

            _exe = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exe)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Exe) ? string.Empty : AppDisplayNames.Resolve(Exe);

    public int LimitMinutes
    {
        get => _limitMinutes;
        set
        {
            if (_limitMinutes == value)
                return;

            _limitMinutes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LimitMinutes)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class LocalCustomGameItem
{
    public required string Exe { get; init; }
    public required string DisplayName { get; init; }
}

internal sealed class LocalWebCategoryItem : INotifyPropertyChanged
{
    private bool _isBlocked;
    private readonly Action<LocalWebCategoryItem>? _onToggled;

    public LocalWebCategoryItem(Action<LocalWebCategoryItem>? onToggled = null) => _onToggled = onToggled;

    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }

    public bool IsBlocked
    {
        get => _isBlocked;
        set
        {
            if (_isBlocked == value)
                return;

            _isBlocked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBlocked)));
            _onToggled?.Invoke(this);
        }
    }

    /// <summary>Sets the toggle without triggering the change callback (used during load).</summary>
    public void SetBlockedSilent(bool value)
    {
        if (_isBlocked == value)
            return;

        _isBlocked = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBlocked)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class LocalHubCard
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string IconGlyph { get; init; }
}

internal sealed class LocalModeChoiceItem
{
    public required string Slug { get; init; }
    public required string Label { get; init; }
}

internal sealed class LocalBedtimeDayOverride
{
    public bool? Enabled { get; set; }
    public string? Time { get; set; }
    public string? WakeTime { get; set; }
}

internal sealed class LocalDayLimitItem : INotifyPropertyChanged
{
    private bool _overrideEnabled;
    private int _minutes = 30;

    public required string DayKey { get; init; }
    public required string DayLabel { get; init; }

    public bool OverrideEnabled
    {
        get => _overrideEnabled;
        set
        {
            if (_overrideEnabled == value) return;
            _overrideEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverrideEnabled)));
        }
    }

    public int Minutes
    {
        get => _minutes;
        set
        {
            if (_minutes == value) return;
            _minutes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Minutes)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class LocalStudyDayOverride
{
    public bool? Enabled { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}

internal sealed class LocalDayBedtimeItem : INotifyPropertyChanged
{
    private bool _overrideEnabled;
    private string _bedtimeTime = "23:00";
    private string _wakeTime = "07:00";

    public required string DayKey { get; init; }
    public required string DayLabel { get; init; }

    public bool OverrideEnabled
    {
        get => _overrideEnabled;
        set
        {
            if (_overrideEnabled == value) return;
            _overrideEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverrideEnabled)));
        }
    }

    public string BedtimeTime
    {
        get => _bedtimeTime;
        set
        {
            if (_bedtimeTime == value) return;
            _bedtimeTime = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BedtimeTime)));
        }
    }

    public string WakeTime
    {
        get => _wakeTime;
        set
        {
            if (_wakeTime == value) return;
            _wakeTime = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WakeTime)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class LocalDayStudyItem : INotifyPropertyChanged
{
    private bool _overrideEnabled;
    private string _startTime = "09:00";
    private string _endTime = "17:00";

    public required string DayKey { get; init; }
    public required string DayLabel { get; init; }

    public bool OverrideEnabled
    {
        get => _overrideEnabled;
        set
        {
            if (_overrideEnabled == value) return;
            _overrideEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverrideEnabled)));
        }
    }

    public string StartTime
    {
        get => _startTime;
        set
        {
            if (_startTime == value) return;
            _startTime = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartTime)));
        }
    }

    public string EndTime
    {
        get => _endTime;
        set
        {
            if (_endTime == value) return;
            _endTime = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EndTime)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

