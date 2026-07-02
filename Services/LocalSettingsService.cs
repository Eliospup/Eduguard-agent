using EduGuardAgent.Agent;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class LocalSettingsService
{
    private readonly LocalSettingsCatalogStore _store = new();
    private readonly AgentModeService _agentMode;
    private readonly BedtimeService _bedtime;
    private readonly GamingTimeTracker _gaming;
    private readonly AppTimeLimitTracker _appTimeLimits;
    private readonly YoutubeTimeTracker _youtube;
    private readonly StudyTimeService _studyTime;
    private readonly PunishmentService _punishment;
    private readonly ImageShieldPolicyService _imageShieldPolicy;
    private readonly ExitPinService _exitPin;
    private readonly UrlBlockingService _urlBlocking;
    private readonly SessionState _sessionState;
    private readonly VpnBlockingService _vpnBlocking;

    private LocalSettingsCatalog _catalog;

    public LocalSettingsService(
        AgentModeService agentMode,
        BedtimeService bedtime,
        GamingTimeTracker gaming,
        AppTimeLimitTracker appTimeLimits,
        YoutubeTimeTracker youtube,
        StudyTimeService studyTime,
        PunishmentService punishment,
        ImageShieldPolicyService imageShieldPolicy,
        ExitPinService exitPin,
        UrlBlockingService urlBlocking,
        SessionState sessionState,
        VpnBlockingService vpnBlocking)
    {
        _agentMode = agentMode;
        _bedtime = bedtime;
        _gaming = gaming;
        _appTimeLimits = appTimeLimits;
        _youtube = youtube;
        _studyTime = studyTime;
        _punishment = punishment;
        _imageShieldPolicy = imageShieldPolicy;
        _exitPin = exitPin;
        _urlBlocking = urlBlocking;
        _sessionState = sessionState;
        _vpnBlocking = vpnBlocking;
        _catalog = _store.Load();
    }

    public LocalSettingsCatalog Catalog => _catalog;

    public string ActiveModeSlug
    {
        get => _catalog.ActiveModeSlug;
        set
        {
            if (!AgentModeSlugs.IsKnown(value))
                return;
            _catalog.ActiveModeSlug = AgentModeSlugs.Normalize(value);
            Persist();
        }
    }

    public LocalPerModeRuleSet RulesFor(string slug) =>
        _catalog.PerMode[AgentModeSlugs.Normalize(slug)];

    public void Persist() => _store.Save(_catalog);

    public void SeedFromRuntimeIfEmpty()
    {
        var active = AgentModeSlugs.Normalize(_agentMode.BaseSlug);
        _catalog.ActiveModeSlug = active;

        var rules = RulesFor(active);
        rules.ScreenTimeDailyLimitMinutes = _agentMode.ScreenTimeLimitMinutes;
        rules.GamingDailyLimitMinutes = _gaming.LimitMinutes;
        rules.GamingShowOverlay = _gaming.ShowPlaytimeOverlay;
        rules.YoutubeDailyLimitMinutes = _youtube.LimitMinutes;
        rules.YoutubeShowOverlay = _youtube.ShowOverlay;
        rules.BedtimeEnabled = _bedtime.Settings.Enabled;
        rules.BedtimeTime = _bedtime.Settings.Time.ToString("HH:mm");
        rules.WakeTime = _bedtime.Settings.WakeTime.ToString("HH:mm");
        if (_bedtime.Settings.Weekly.Count > 0)
        {
            rules.BedtimeWeekly = _bedtime.Settings.Weekly.ToDictionary(
                p => DayScheduleKeys.Format(p.Key),
                p => new LocalBedtimeDayOverride
                {
                    Enabled = p.Value.Enabled,
                    Time = p.Value.Time?.ToString("HH:mm"),
                    WakeTime = p.Value.WakeTime?.ToString("HH:mm"),
                },
                StringComparer.OrdinalIgnoreCase);
        }
        rules.StudyEnabled = _studyTime.Settings.Enabled;
        rules.StudyStart = _studyTime.Settings.StartTime.ToString("HH:mm");
        rules.StudyEnd = _studyTime.Settings.EndTime.ToString("HH:mm");
        rules.StudyDays = _studyTime.Settings.Days.Select(DayScheduleKeys.Format).ToList();
        if (_studyTime.Settings.Weekly.Count > 0)
        {
            rules.StudyWeekly = _studyTime.Settings.Weekly.ToDictionary(
                p => DayScheduleKeys.Format(p.Key),
                p => new LocalStudyDayOverride
                {
                    Enabled = true,
                    StartTime = p.Value.StartTime?.ToString("HH:mm"),
                    EndTime = p.Value.EndTime?.ToString("HH:mm"),
                },
                StringComparer.OrdinalIgnoreCase);
        }

        rules.StudyBlockGames = _studyTime.Settings.BlockGames;
        rules.StudyBlockYoutube = _studyTime.Settings.BlockYoutube;
        rules.StudyBlockDistractingSites = _studyTime.Settings.BlockDistractingSites;
        rules.StudyBlockDistractingApps = _studyTime.Settings.BlockDistractingApps;
        rules.BlockTaskManager = _agentMode.Features.BlockTaskManager;
        rules.VpnShield = _agentMode.Features.VpnShield;
        rules.BlockRegistryEditor = _agentMode.Features.BlockRegistryEditor;
        rules.BlockCommandPrompt = _agentMode.Features.BlockCommandPrompt;
        rules.BlockPowerShell = _agentMode.Features.BlockPowerShell;
        rules.BlockSystemConfig = _agentMode.Features.BlockSystemConfig;
        rules.BlockControlPanel = _agentMode.Features.BlockControlPanel;
        rules.BlockProcessTools = _agentMode.Features.BlockProcessTools;
        rules.BlockProcessKillers = _agentMode.Features.BlockProcessKillers;
        rules.KioskMode = _agentMode.Features.KioskMode;

        _catalog.ImageShieldEnabled = _imageShieldPolicy.GlobalEnabled;
        _catalog.YouTubeRestrictedModeEnabled = _youtube.RestrictedModeEnabled;
        _catalog.BlueLightFilterEnabled = _bedtime.Settings.BlueLightFilterEnabled;
        _catalog.ShowGamingTimer = _gaming.ShowPlaytimeOverlay;
        _catalog.ShowYoutubeTimer = _youtube.ShowOverlay;
        _catalog.BlockedHosts = _urlBlocking.BlockedHosts.ToList();
        _catalog.BlockedApps = _sessionState.GetBlockedAppsExcept(AppBlockCategory.VpnShield).ToList();
        EnsureExitPinSeeded();
        Persist();
    }

    public void EnsureExitPinSeeded()
    {
        // No-op: the exit PIN is no longer mirrored into the catalog (which is plaintext on
        // disk). The hashed verifier in ExitPinStorage is the single source of truth.
    }

    public void ApplyActiveMode()
    {
        var slug = ActiveModeSlug;
        var rules = RulesFor(slug);

        _agentMode.Apply(
            new ModeSettingsPayload { Slug = slug, Features = rules.ToFeaturesPayload() },
            rules.ToScreenTimePayload());

        _bedtime.Update(BedtimeFromLocalRules(rules));

        _gaming.ApplySettings(rules.ToGamingPayload());
        _appTimeLimits.ApplyLimits(rules.AppTimeLimits);
        _youtube.ApplySettings(rules.ToYoutubePayload());
        _youtube.SetRestrictedModeEnabled(_catalog.YouTubeRestrictedModeEnabled);

        _studyTime.Update(StudyTimeFromLocalRules(rules));

        ApplyGlobalSettings();

        foreach (var app in _catalog.BlockedApps)
            _sessionState.BlockApp(app, AppBlockCategory.DomManual);

        if (_catalog.BlockedHosts.Count > 0)
            _urlBlocking.BlockMany(_catalog.BlockedHosts);

        if (rules.VpnShield)
            _vpnBlocking.Enable(_sessionState, _urlBlocking);
        else
            _vpnBlocking.Disable(_sessionState, _urlBlocking);
    }

    private BedtimeSettings BedtimeFromLocalRules(LocalPerModeRuleSet rules)
    {
        Dictionary<string, BedtimeDayPayload>? weekly = null;
        if (rules.BedtimeWeekly?.Count > 0)
        {
            weekly = rules.BedtimeWeekly.ToDictionary(
                p => p.Key,
                p => new BedtimeDayPayload
                {
                    Enabled = p.Value.Enabled,
                    Time = p.Value.Time,
                    WakeTime = p.Value.WakeTime,
                },
                StringComparer.OrdinalIgnoreCase);
        }

        return BedtimeSettings.FromPayload(
            rules.BedtimeEnabled,
            rules.BedtimeTime,
            rules.WakeTime,
            weekly: weekly,
            blueLightFilterEnabled: _catalog.BlueLightFilterEnabled);
    }

    private StudyTimeSettings StudyTimeFromLocalRules(LocalPerModeRuleSet rules)
    {
        var start = BedtimeSettings.TryParseTime(rules.StudyStart, out var parsedStart)
            ? parsedStart
            : new TimeOnly(9, 0);
        var end = BedtimeSettings.TryParseTime(rules.StudyEnd, out var parsedEnd)
            ? parsedEnd
            : new TimeOnly(17, 0);

        return new StudyTimeSettings
        {
            Enabled = rules.StudyEnabled,
            StartTime = start,
            EndTime = end,
            Days = StudyTimeSettings.ParseDays(rules.StudyDays),
            Weekly = BuildStudyWeeklyFromRules(rules.StudyWeekly),
            BlockGames = rules.StudyBlockGames,
            BlockYoutube = rules.StudyBlockYoutube,
            BlockDistractingSites = rules.StudyBlockDistractingSites,
            BlockDistractingApps = rules.StudyBlockDistractingApps,
        };
    }

    private static Dictionary<DayOfWeek, StudyDayConfig> BuildStudyWeeklyFromRules(
        Dictionary<string, LocalStudyDayOverride>? weekly)
    {
        if (weekly is null || weekly.Count == 0)
            return new Dictionary<DayOfWeek, StudyDayConfig>();

        var result = new Dictionary<DayOfWeek, StudyDayConfig>();
        foreach (var (key, day) in weekly)
        {
            if (day is null || !DayScheduleKeys.TryParse(key, out var dow))
                continue;

            result[dow] = new StudyDayConfig
            {
                Enabled = day.Enabled ?? true,
                StartTime = BedtimeSettings.TryParseTime(day.StartTime, out var parsedStart)
                    ? parsedStart
                    : null,
                EndTime = BedtimeSettings.TryParseTime(day.EndTime, out var parsedEnd)
                    ? parsedEnd
                    : null,
            };
        }

        return result;
    }

    public void ApplyGlobalSettings()
    {
        // Appearance is global and overrides the per-mode overlay flags: it is the single
        // source of truth for whether the on-screen timers are drawn. Applied after the
        // per-mode gaming/youtube ApplySettings in ApplyActiveMode, so the global value wins.
        _gaming.SetShowPlaytimeOverlay(_catalog.ShowGamingTimer);
        _youtube.SetShowOverlay(_catalog.ShowYoutubeTimer);

        var payload = ToImageShieldPayload();
        _imageShieldPolicy.ApplyLocal(payload);

        ApplyExitPinFromCatalog();

        _punishment.ApplySettings(new PunishmentSettingsPayload
        {
            Enabled = _catalog.PunishmentEnabled,
            ThresholdTrustedToSub = ResolveThreshold(
                _catalog.PunishmentThresholdTrustedToSub,
                _catalog.PunishmentInfractionThreshold),
            ThresholdSubToRestricted = ResolveThreshold(
                _catalog.PunishmentThresholdSubToRestricted,
                _catalog.PunishmentInfractionThreshold),
            EscalationHours = _catalog.PunishmentEscalationHours,
            EscalationMinutes = _catalog.PunishmentEscalationMinutes,
            InfractionExtensions = _catalog.PunishmentExtensions.ToSettings().ToPayload(),
            RegenPerHour = _catalog.TrustRegenPerHour,
            InfractionWeights = new InfractionWeightsPayload
            {
                VpnAttempt = _catalog.TrustWeightVpn,
                BypassAttempt = _catalog.TrustWeightBypass,
                BlockedAppRepeated = _catalog.TrustWeightBlockedApp,
                BlockedSearch = _catalog.TrustWeightBlockedSearch,
                StudyTimeViolation = _catalog.TrustWeightStudy,
                LimitIgnored = _catalog.TrustWeightLimit,
            },
            InfractionKinds = new InfractionKindsPayload
            {
                VpnAttempt = _catalog.InfractionVpnAttempt,
                BlockedAppRepeated = _catalog.InfractionBlockedAppRepeated,
                BypassAttempt = _catalog.InfractionBypassAttempt,
                LimitIgnored = _catalog.InfractionLimitIgnored,
                StudyTimeViolation = _catalog.InfractionStudyTimeViolation,
                BlockedSearch = _catalog.InfractionBlockedSearch,
            },
        });
    }

    private static int ResolveThreshold(int specific, int legacy) =>
        specific > 0 ? specific : Math.Max(1, legacy);

    private void ApplyExitPinFromCatalog()
    {
        // The exit PIN is managed solely through ExitPinStorage (the dashboard's Save/Remove).
        // A value left in the catalog by an older build must NEVER re-inject or clear the
        // active PIN — that caused a stale PIN to come back on every restart. Just scrub it.
        if (_catalog.ExitPin is not null)
        {
            _catalog.ExitPin = null;
            SaveCatalog();
        }
    }

    public void HydrateImageShieldCatalogFromPolicy()
    {
        var state = ImageShieldPolicyStore.Load() ?? ImageShieldPolicyState.LocalDefaults();

        _catalog.ImageShieldPerMode ??= ToggleDictFromModes(state.PerMode);
        _catalog.ImageShieldPerBrowser ??= ToggleDictFromBrowsers(state.PerBrowser);
        _catalog.ImageShieldMinSize ??= state.MinSize ?? 80;
        _catalog.ImageShieldNsfwThreshold ??= state.NsfwThreshold ?? 0.45;
        _catalog.ImageShieldMaxPerSecond ??= state.MaxPerSecond ?? 24;
        _catalog.ImageShieldSexyWeight ??= state.SexyWeight ?? 1.0;
    }

    private static Dictionary<string, bool> ToggleDictFromModes(
        Dictionary<string, ImageShieldToggleState> source)
    {
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in new[] { AgentModeSlugs.TrustedSub, AgentModeSlugs.Sub, AgentModeSlugs.RestrictedSub })
        {
            dict[mode] = source.TryGetValue(mode, out var toggle) ? toggle.Enabled : true;
        }

        return dict;
    }

    private static Dictionary<string, bool> ToggleDictFromBrowsers(
        Dictionary<string, ImageShieldToggleState> source)
    {
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[]
                 {
                     ImageShieldBrowserKeys.Firefox,
                     ImageShieldBrowserKeys.Chrome,
                     ImageShieldBrowserKeys.Edge,
                     ImageShieldBrowserKeys.Brave,
                 })
        {
            var fallback = key switch
            {
                ImageShieldBrowserKeys.Firefox => true,
                ImageShieldBrowserKeys.Chrome => Config.ExtensionGuardEnforceChromium,
                _ => false,
            };
            dict[key] = source.TryGetValue(key, out var toggle) ? toggle.Enabled : fallback;
        }

        return dict;
    }

    public ImageShieldSettingsPayload ToImageShieldPayload()
    {
        Dictionary<string, ImageShieldTogglePayload>? perMode = null;
        if (_catalog.ImageShieldPerMode is { Count: > 0 })
        {
            perMode = _catalog.ImageShieldPerMode.ToDictionary(
                p => p.Key,
                p => new ImageShieldTogglePayload { Enabled = p.Value },
                StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, ImageShieldTogglePayload>? perBrowser = null;
        if (_catalog.ImageShieldPerBrowser is { Count: > 0 })
        {
            perBrowser = _catalog.ImageShieldPerBrowser.ToDictionary(
                p => p.Key,
                p => new ImageShieldTogglePayload { Enabled = p.Value },
                StringComparer.OrdinalIgnoreCase);
        }

        return new ImageShieldSettingsPayload
        {
            Enabled = _catalog.ImageShieldEnabled,
            PerMode = perMode,
            PerBrowser = perBrowser,
            MinSize = _catalog.ImageShieldMinSize,
            NsfwThreshold = _catalog.ImageShieldNsfwThreshold,
            SexyWeight = _catalog.ImageShieldSexyWeight,
            MaxPerSecond = _catalog.ImageShieldMaxPerSecond,
        };
    }

    public void SaveRulesFor(string slug, LocalPerModeRuleSet rules)
    {
        _catalog.PerMode[AgentModeSlugs.Normalize(slug)] = rules;
        Persist();
        if (string.Equals(ActiveModeSlug, slug, StringComparison.OrdinalIgnoreCase))
            ApplyActiveMode();
    }

    public void SaveCatalog()
    {
        Persist();
        ApplyActiveMode();
    }

    public static IReadOnlyList<LocalHubCard> HubCards { get; } =
    [
        new() { Key = "supervision", Title = "Supervision", Subtitle = "Mode, screen time & feature flags.", IconGlyph = "🛡️" },
        new() { Key = "kiosk", Title = "Kiosk apps", Subtitle = "Detected apps allowed in kiosk mode.", IconGlyph = "🖥️" },
        new() { Key = "control", Title = "Control", Subtitle = "Lock, logoff, kill apps, message.", IconGlyph = "🎛️" },
        new() { Key = "screenshots", Title = "Screenshots", Subtitle = "Timeline + on-demand capture.", IconGlyph = "📷" },
        new() { Key = "blocklist", Title = "Blocklist", Subtitle = "Permanently blocked apps & sites.", IconGlyph = "🚫" },
        new() { Key = "bedtime", Title = "Sleepy time", Subtitle = "Auto lock/unlock schedule.", IconGlyph = "🌙" },
        new() { Key = "playtime", Title = "Play time", Subtitle = "Daily global game limit & overlay.", IconGlyph = "🎮" },
        new() { Key = "app_limits", Title = "App time limits", Subtitle = "Daily limits for specific apps.", IconGlyph = "⏱️" },
        new() { Key = "youtube", Title = "YouTube", Subtitle = "Daily YouTube limit & overlay.", IconGlyph = "▶️" },
        new() { Key = "study", Title = "Study time", Subtitle = "Block games during study hours.", IconGlyph = "📖" },
        new() { Key = "discipline", Title = "Discipline", Subtitle = "Auto-escalation on rule-breaking.", IconGlyph = "⚖️" },
        new() { Key = "image_shield", Title = "Image Shield", Subtitle = "NSFW blur — Firefox today, per mode & browser.", IconGlyph = "👁️" },
        new() { Key = "appearance", Title = "Appearance", Subtitle = "Hide Guardi's on-screen visuals: widget, timers, badges.", IconGlyph = "🎨" },
        new() { Key = "security", Title = "Security", Subtitle = "Exit PIN and more.", IconGlyph = "🔒" },
    ];
}
