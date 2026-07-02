using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using EduGuardAgent.Agent;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;
using EduGuardAgent.Services;
using EduGuardAgent.Views;

namespace EduGuardAgent.ViewModels;

internal enum SoftLimitKind
{
    ScreenTime,
    Youtube,
    Bedtime,
}

[SupportedOSPlatform("windows")]
internal sealed partial class MainViewModel : INotifyPropertyChanged, IAgentNotifier, IDisposable
{
    private readonly AgentHost _host;
    private readonly SessionState _sessionState;
    private readonly AgentModeService _agentMode;
    private readonly UrlBlockingService _urlBlocking;
    private readonly WebContentFilterService _webContentFilter;
    private readonly SafeSearchService _safeSearch;
    private readonly YouTubeRestrictedModeService _youtubeRestricted;
    private readonly ImageBlurExtensionService _imageShield;
    private readonly ImageShieldPolicyService _imageShieldPolicy;
    private readonly ExtensionEnforcementService _extensionGuard;
    private readonly ExtensionPolicyWatchdog _extensionPolicyWatchdog;
    private readonly VpnBlockingService _vpnBlocking;
    private readonly ScreenTimeTracker _screenTime;
    private readonly StudyTimeService _studyTime;
    private readonly StudyDistractionGuard _studyDistractionGuard;
    private readonly GamingTimeTracker _gaming;
    private readonly AppTimeLimitTracker _appTimeLimits;
    private readonly YoutubeTimeTracker _youtube;
    private readonly PunishmentService _punishment;
    private readonly ExtensionInfractionWatcher _extensionInfractionWatcher;
    private readonly ExtensionInfractionHttpReporter _extensionInfractionHttp;
    private readonly BedtimeService _bedtime;
    private readonly ExitPinService _exitPin;
    private readonly KioskAppRegistry _kioskApps;
    private readonly KioskService _kiosk;
    private readonly LocalModeService _localMode;
    private readonly LocalSettingsService _localSettings;
    private readonly SubProfileService _subProfile;
    private bool _kioskBypassed;
    private bool _kioskPresentationActive;
    private readonly Dispatcher _dispatcher;
    private RestrictionItem? _playTimeRestriction;
    private RestrictionItem? _youtubeTimeRestriction;
    private RestrictionItem? _studyTimeRestriction;
    private bool _isEnrolled;
    private bool _isConnecting;
    private bool _isOnline;
    private bool _isScreenTimeLocked;
    private bool _isDomLocked;
    private bool _isBedtimeLocked;
    private bool _isSoftLimitWarningVisible;
    private bool _screenTimeLockBypassed;
    private bool _softLimitWarningBypassed;
    private bool _youtubeSoftLimitWarningBypassed;
    private bool _gamingSoftLimitWarningBypassed;
    private bool _gamingSoftLimitOverlayOpen;
    private int _gamingNagTickCount;
    private int _youtubeNagTickCount;
    private const int NagEveryTicks = 3; // nag every 3 minutes
    private bool _bedtimeSoftWarningBypassed;
    private SoftLimitKind _softLimitWarningKind;
    private double? _lastScreenTimeLimitMinutesForBypass;
    private bool _bedtimeLockBypassed;
    private string _bedtimeUnlockCountdown = "--:--";
    private DispatcherTimer? _bedtimeCountdownTimer;
    private DispatcherTimer? _bedtimeOverrunTimer;
    private string _punishmentDeescalationCountdown = "--:--";
    private bool _showPunishmentCountdown;
    private DispatcherTimer? _punishmentCountdownTimer;
    private bool _disposed;
    private volatile bool _coreShutdownDone;
    private int _restrictionsRefreshSerial;
    private CancellationTokenSource? _startupCts;
    private string _statusText = UiCopy.StatusOffline;
    private string _focusedWindow = "—";
    private int _runningAppsCount;
    private string _lastDomMessage = UiCopy.DefaultDomMessage;
    private string _enrollmentCode = string.Empty;
    private string _pcName = string.Empty;
    private string _enrollmentDetail = string.Empty;
    private string _screenTimeUsed = "0m";
    private string _screenTimeLimit = "5h";
    private double _screenTimeProgress;
    private string _localBlockedUrl = string.Empty;
    private string _localBlockedApp = string.Empty;
    private string _localKioskManualPath = string.Empty;
    private string _localKioskManualName = string.Empty;
    private bool _isLocalSyncing;
    private string _localSyncWarning = string.Empty;
    private string _localSectionKey = string.Empty;
    private string _localEditingModeSlug = AgentModeSlugs.Sub;
    private string _localActiveModeSlug = AgentModeSlugs.Sub;
    private string _localStudyDaysText = "mon,tue,wed,thu,fri";
    private string _localExitPin = string.Empty;
    private string _localSubDisplayName = string.Empty;
    private int _localScreenshotInterval = Config.ScreenshotIntervalMinutes;
    private int _localPunishmentThresholdTrustedToSub = 3;
    private int _localPunishmentThresholdSubToRestricted = 3;
    private int _localPunishmentEscalationHours = 24;
    private int _localPunishmentEscalationMinutes;
    private PunishmentExtensionCatalog _localPunishmentExtensions = PunishmentExtensionCatalog.CreateDefaults();
    private bool _localPunishmentEnabled = true;
    private int _localTrustRegenPerHour = 5;
    private int _localTrustWeightVpn = 25;
    private int _localTrustWeightBypass = 20;
    private int _localTrustWeightBlockedApp = 15;
    private int _localTrustWeightBlockedSearch = 15;
    private int _localTrustWeightStudy = 10;
    private int _localTrustWeightLimit = 10;
    private bool _localGamingShowOverlay = true;
    private bool _localYoutubeShowOverlay = true;
    private bool _localBlockTaskManager;
    private bool _localVpnShield = true;
    private bool _localBlockRegistryEditor;
    private bool _localBlockCommandPrompt;
    private bool _localBlockPowerShell;
    private bool _localBlockSystemConfig;
    private bool _localBlockControlPanel;
    private bool _localBlockProcessTools;
    private bool _localBlockProcessKillers = true;
    private bool _localKioskMode;
    private bool _wasStudyActiveForToast;
    private bool _localStudyEnabled;
    private string _localStudyStart = "09:00";
    private string _localStudyEnd = "17:00";
    private string _currentTime = DateTime.Now.ToString("HH:mm");

    public MainViewModel()
    {
        _sessionState = new SessionState();
        _agentMode = new AgentModeService(_sessionState);
        _urlBlocking = new UrlBlockingService();
        _webContentFilter = new WebContentFilterService(_urlBlocking);
        _safeSearch = new SafeSearchService();
        _youtubeRestricted = new YouTubeRestrictedModeService();
        _imageShield = new ImageBlurExtensionService();
        _imageShieldPolicy = new ImageShieldPolicyService();
        _vpnBlocking = new VpnBlockingService();
        _exitPin = new ExitPinService();
        _exitPin.LoadFromStorage();
        _dispatcher = Application.Current.Dispatcher;
        _extensionGuard = new ExtensionEnforcementService(
            log: msg =>
            {
                AuditLog.Write($"Extension guard: {msg}");
                PostOnUi(() => AddLog(msg));
            },
            ensureBrowser: browser =>
            {
                if (_imageShield.IsConfigured)
                    _imageShield.EnsureBrowser(browser);
            },
            prepareFirefoxUpgrade: () =>
            {
                if (_imageShield.IsConfigured)
                    _imageShield.PrepareFirefoxExtensionUpgrade(msg => PostOnUi(() => AddLog(msg)));
            });
        // Autonomous, browser-independent guard: rewrites distribution/policies.json the moment
        // an admin-level user deletes it, so the shield can't be unloaded by wiping the policy
        // file between (or before) browser launches. Self-gates on the shield being enabled.
        _extensionPolicyWatchdog = new ExtensionPolicyWatchdog(
            shouldEnforce: () =>
                _imageShieldPolicy.IsEffectivelyEnabled(_agentMode.Slug)
                && ImageShieldRuntimeStore.IsFilteringActive,
            firefoxAddonId: () => ExtensionConfigResolver.Active?.FirefoxAddonId,
            repairFirefox: () =>
            {
                if (!_imageShield.IsConfigured)
                    return;
                var settings = _imageShieldPolicy.BuildTuningSettings(_agentMode.Slug);
                if (_imageShield.HasPersistedInstall)
                {
                    _imageShield.SetRuntimeActive(true, settings);
                    _imageShield.ApplyFirefoxSignedEnterprisePolicies(settings, requireDomToggle: false);
                }
                else
                {
                    _imageShield.Apply(settings);
                }
            },
            chromiumTarget: () =>
            {
                if (!Config.ExtensionGuardEnforceChromium || ChromiumUnpackedMode.IsActive)
                    return null;
                var cfg = ExtensionConfigResolver.Active;
                if (cfg is null || !cfg.IsChromiumReady || string.IsNullOrWhiteSpace(cfg.ChromeUpdateUrl))
                    return null;
                return (cfg.ChromiumExtensionId, cfg.ChromeUpdateUrl);
            },
            log: msg =>
            {
                AuditLog.Write($"Extension policy watchdog: {msg}");
                PostOnUi(() => AddLog(msg));
            });
        _extensionPolicyWatchdog.Start();
        BrowserInstallOrchestrator.RestartCountdownHandler = (browser, message, seconds) =>
            PostOnUi(() => BrowserRestartCountdownRequested?.Invoke(browser, message, seconds));
        _screenTime = new ScreenTimeTracker(_dispatcher);
        _studyTime = new StudyTimeService(_dispatcher);
        _studyDistractionGuard = new StudyDistractionGuard(_studyTime, _urlBlocking, _dispatcher);
        _studyDistractionGuard.DistractingAppBlocked += OnStudyDistractionAppBlocked;
        _wasStudyActiveForToast = _studyTime.IsActiveNow;
        _gaming = new GamingTimeTracker(_dispatcher, _studyTime, () => IsHardDisciplineEnforcement);
        _appTimeLimits = new AppTimeLimitTracker(_dispatcher, () => IsHardDisciplineEnforcement);
        _youtube = new YoutubeTimeTracker(_dispatcher, _studyTime, () => IsHardDisciplineEnforcement);
        _punishment = new PunishmentService(_dispatcher, () => _agentMode.BaseStrictnessIndex);
        _extensionInfractionWatcher = new ExtensionInfractionWatcher(OnExtensionBlockedSearch);
        _extensionInfractionWatcher.Start();
        _extensionInfractionHttp = new ExtensionInfractionHttpReporter(
            OnExtensionBlockedSearch,
            getShieldState: BuildAgentShieldState,
            onYoutubeSoftAck: OnYoutubeSoftAck);
        _extensionInfractionHttp.Start();
        NativeMessagingHostRegistry.Register();
        _localMode = new LocalModeService();
        _localMode.LoadFromStorage();
        _localMode.Changed += OnLocalModeChanged;
        _subProfile = new SubProfileService();
        _subProfile.LoadFromStorage();
        _subProfile.Changed += OnSubProfileChanged;
        _host = new AgentHost(this, _sessionState, _urlBlocking, _exitPin, _gaming, _youtube, _agentMode, _punishment, _localMode);
        _bedtime = new BedtimeService(_dispatcher);
        _localSettings = new LocalSettingsService(
            _agentMode, _bedtime, _gaming, _appTimeLimits, _youtube, _studyTime, _punishment,
            _imageShieldPolicy, _exitPin, _urlBlocking, _sessionState, _vpnBlocking);
        _kioskApps = new KioskAppRegistry();
        _kiosk = new KioskService(_kioskApps);
        _kiosk.Log += message => PostOnUi(() => AddLog(message));
        _kioskApps.Changed += RefreshKioskApps;
        RefreshKioskApps();
        _pcName = $"{Environment.MachineName}";

        RefreshModeSteps();

        RefreshRestrictions();
        RefreshTodayRules();

        EnrollCommand = new RelayCommand(async () => await EnrollAsync(), () => CanEnroll);
        ResetEnrollmentCommand = new RelayCommand(ResetEnrollment, () => IsEnrolled && !IsConnecting);
        LaunchKioskAppCommand = new RelayCommand<KioskAppItem>(LaunchKioskApp, app => app is not null);
        RequestKioskExitCommand = new RelayCommand(RequestKioskExit);
        NavigateToKioskAppsCommand = new RelayCommand(NavigateToKioskApps, () => IsKioskActive);
        DismissLockOverlayCommand = new RelayCommand(DismissLockOverlay, () => IsLockOverlayVisible);
        ContinueSoftLimitAnywayCommand = new RelayCommand(ContinueSoftLimitAnyway, () => true);
        OpenRestrictionDetailCommand = new RelayCommand<string>(OpenRestrictionDetail, _ => true);
        NavigateBackCommand = new RelayCommand(NavigateBack, () => !IsHomePage);
        ToggleLocalModeCommand = new RelayCommand(async () => await ToggleLocalModeAsync(), () => !IsLocalSyncing);
        NavigateToLocalSettingsCommand = new RelayCommand(NavigateToLocalSettings, () => IsLocalMode);
        OpenLocalSectionCommand = new RelayCommand<string>(OpenLocalSection, key => IsLocalMode && !string.IsNullOrWhiteSpace(key));
        SaveLocalSectionCommand = new RelayCommand(SaveLocalSection, () => IsLocalMode);
        BlockLocalUrlCommand = new RelayCommand(BlockLocalUrl, () => IsLocalMode && !string.IsNullOrWhiteSpace(LocalBlockedUrl));
        BlockLocalAppCommand = new RelayCommand(BlockLocalApp, () => IsLocalMode && !string.IsNullOrWhiteSpace(LocalBlockedApp));
        LocalLockScreenCommand = new RelayCommand(TriggerLocalLockScreen, () => IsLocalMode);

        foreach (var card in LocalSettingsService.HubCards)
            LocalHubCards.Add(card);

        InitializeLocalDashboardCommands();
        InitializeAutoStart();
        InitializeWidgetPrompts();

        _sessionState.Changed += RefreshRestrictions;
        _urlBlocking.Changed += RefreshRestrictions;
        _vpnBlocking.Changed += RefreshRestrictions;
        _screenTime.ElapsedChanged += OnScreenTimeElapsed;
        _gaming.UsageChanged += OnGamingUsageChanged;
        _gaming.LimitReached += OnGamingLimitReached;
        _gaming.GameSessionStarted += OnGameSessionStarted;
        _gaming.StudyModeBlocked += OnStudyModeBlocked;
        _gaming.HudStateChanged += state => PostOnUi(() => GamingHudStateChanged?.Invoke(state));
        _appTimeLimits.UsageChanged += OnAppTimeLimitsChanged;
        _appTimeLimits.LimitReached += OnAppTimeLimitReached;
        _youtube.UsageChanged += OnYoutubeUsageChanged;
        _youtube.LimitReached += OnYoutubeLimitReached;
        _youtube.StudyModeBlocked += OnYoutubeStudyModeBlocked;
        _youtube.HudStateChanged += state => PostOnUi(() => YoutubeHudStateChanged?.Invoke(state));
        _studyTime.SettingsChanged += OnStudyTimeSettingsChanged;
        _studyTime.ActiveStateChanged += OnStudyTimeActiveStateChanged;
        _agentMode.Changed += OnAgentModeChanged;
        _agentMode.SecurityToolBlocked += OnSecurityToolBlocked;
        _punishment.InfractionRegistered += OnInfractionRegistered;
        _punishment.Escalated += OnPunishmentEscalated;
        _punishment.FloorLevelChanged += OnPunishmentFloorChanged;
        _punishment.StateChanged += OnPunishmentStateChanged;
        _bedtime.WarningDue += OnBedtimeWarningDue;
        _bedtime.BlueLightFilterActiveChanged += OnBlueLightFilterActiveChanged;
        _bedtime.BedtimeReached += OnBedtimeReached;
        _bedtime.WakeTimeReached += OnWakeTimeReached;
        _bedtime.SettingsChanged += OnBedtimeSettingsChanged;
        _extensionGuard.StateChanged += OnExtensionGuardStateChanged;
        _extensionGuard.UnsupportedBrowserBlocked += OnUnsupportedBrowserBlocked;

        _agentMode.SetPunishmentFloor(_punishment.FloorLevelIndex);
        SyncPunishmentCountdownTimer();

        foreach (var r in _punishment.RecentInfractions)
            Infractions.Add(new InfractionItem
            {
                Label = InfractionKindLabel(r.Kind),
                Detail = r.Detail,
                Time = r.At.ToLocalTime().ToString("HH:mm"),
                TrustPointsLost = r.TrustPointsLost,
            });

        RefreshDisciplineStanding();

        _dispatcher.Invoke(() =>
        {
            var clock = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) =>
            {
                CurrentTime = DateTime.Now.ToString("HH:mm");
            }, _dispatcher);
            clock.Start();
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ScreenTimeLocked;
    public event Action? ScreenTimeLockDismissed;
    public event Action? SoftLimitWarningRequested;
    public event Action? SoftLimitWarningDismissed;
    public event Action? LockOverlayChanged;
    public event Action<string>? DomMessagePopupRequested;
    public event Action<string, string>? AppBlockedPopupRequested;
    public event Action<string, string>? BedtimeWarningPopupRequested;
    public event Action<string, string>? StudyToastPopupRequested;
    public event Action<bool, BlueLightFilterPhase>? BlueLightFilterStateChanged;
    public event Action<GamingHudState?>? GamingHudStateChanged;
    public event Action<GamingSessionToast>? GamingSessionToastRequested;
    public event Action<YoutubeHudState?>? YoutubeHudStateChanged;
    public event Action<bool>? KioskStateChanged;

    public ObservableCollection<KioskAppItem> KioskApps { get; } = [];
    public ObservableCollection<RestrictionItem> Restrictions { get; } = [];
    public ObservableCollection<LevelStep> LevelSteps { get; } = [];
    public ObservableCollection<InfractionItem> Infractions { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];
    public ObservableCollection<string> DetailItems { get; } = [];
    public ObservableCollection<GameUsageItem> GameUsages { get; } = [];
    public ObservableCollection<LocalHubCard> LocalHubCards { get; } = [];

    private DashboardPage _currentPage = DashboardPage.Home;

    public string LevelTitle => _agentMode.DisplayName;
    public string AppSubtitle => UiCopy.AppSubtitle;
    public string LevelSubtitle => _agentMode.ModeSubtitle;
    public string SubDisplayName => _subProfile.DisplayName ?? string.Empty;
    public bool HasSubDisplayName => _subProfile.HasDisplayName;
    public string PersonalizedName => _subProfile.ResolveDisplayName(_agentMode.Copy);
    public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public string HeaderCenterTitle => IsLocalAreaPage
        ? LocalHeaderTitle
        : IsKioskAppsPage
            ? KioskCopy.AppsTitle
            : LevelTitle;

    public string HeaderCenterSubtitle => IsLocalAreaPage
        ? LocalHeaderSubtitle
        : IsKioskAppsPage
            ? KioskCopy.AppsPageHeaderSubtitle
            : LevelSubtitle;

    // The home page's hero card greets the Sub by name; the slim top bar (above) shows the mode name instead so the two don't repeat each other.
    public string HomeGreetingTitle => HasSubDisplayName ? $"Hi, {PersonalizedName}!" : LevelTitle;

    public string HomeGreetingSubtitle => HasSubDisplayName ? MascotGreeting : LevelSubtitle;
    public bool ShowMascot => _agentMode.Ui.ShowMascot;
    public bool IsMatureUi => !IsSubMode;
    public bool IsSubMode => string.Equals(_agentMode.Slug, AgentModeSlugs.Sub, StringComparison.Ordinal);
    public bool IsTrustedSubMode => string.Equals(_agentMode.Slug, AgentModeSlugs.TrustedSub, StringComparison.Ordinal);
    public bool IsRestrictedSubMode => string.Equals(_agentMode.Slug, AgentModeSlugs.RestrictedSub, StringComparison.Ordinal);
    public string HeaderIconGlyph => _agentMode.Ui.HeaderIconGlyph;
    public string StandingLabel => UiCopy.StandingLabel(SubDisplayName);
    public string MascotGreeting => UiCopy.MascotGreeting(SubDisplayName);
    public string SecurityZoneTitle => UiCopy.SecurityZoneTitle;
    public string SecurityZoneBody => UiCopy.SecurityZoneBody;
    public string ScreenTimeTitle => UiCopy.ScreenTimeTitle;
    public string InfractionsTitle => UiCopy.InfractionsTitle;
    public string InfractionsGood => UiCopy.InfractionsGood;
    public string InfractionsWarning => UiCopy.InfractionsWarning;
    public string BedtimeTitle => UiCopy.BedtimeTitle;
    public string DomMessageTitle => UiCopy.DomMessageTitle;
    public string RestrictionsTitle => UiCopy.RestrictionsTitle;
    public string ActivityTitle => UiCopy.ActivityTitle;
    public string ActivityLogTitle => UiCopy.ActivityLogTitle;
    public string PlayTimeDetailTotalLabel => UiCopy.PlayTimeDetailTotalLabel;
    public string GameListIconGlyph => UiCopy.GameListIconGlyph;
    public string EnrollmentTitle => UiCopy.EnrollmentTitle;
    public string EnrollmentBody => UiCopy.EnrollmentBody;
    public string EnrollmentLevelLabel => UiCopy.EnrollmentLevelLabel;
    public string EnrollmentCodeLabel => UiCopy.EnrollmentCodeLabel;
    public string EnrollmentNameLabel => UiCopy.EnrollmentNameLabel;
    public string EnrollmentButton => UiCopy.EnrollmentButton;
    public string ScreenTimeLimitText => $"of {ScreenTimeLimit} {UiCopy.ScreenTimeLimitSuffix}";

    public string ScreenTimeRemainingText =>
        ScreenTimeUsedMinutes >= ScreenTimeLimitMinutes
            ? UiCopy.ScreenTimeLimitReachedToday
            : string.Format(
                UiCopy.ScreenTimeRemainingFormat,
                FormatDuration(TimeSpan.FromMinutes(Math.Max(0, ScreenTimeLimitMinutes - ScreenTimeUsedMinutes))));

    public bool ShowWidgetScreenTime => IsEnrolled || IsLocalMode;

    public string WidgetScreenTimeRemaining =>
        !ShowWidgetScreenTime
            ? "0m"
            : ScreenTimeUsedMinutes >= ScreenTimeLimitMinutes
                ? UiCopy.HudTimesUpLabel
                : FormatDuration(TimeSpan.FromMinutes(Math.Max(0, ScreenTimeLimitMinutes - ScreenTimeUsedMinutes)));

    public bool WidgetScreenTimeExhausted =>
        ShowWidgetScreenTime && ScreenTimeUsedMinutes >= ScreenTimeLimitMinutes;

    public string WidgetToolTip =>
        ShowWidgetScreenTime
            ? $"Open Guardi — {PersonalizedName} · {SupervisionStatusLabel} · {ScreenTimeRemainingText}"
            : $"Open Guardi — {PersonalizedName} · {SupervisionStatusLabel}";
    public string RunningAppsLabel => UiCopy.ActivityAppsFormat(RunningAppsCount);

    public int InfractionCount => _punishment.InfractionCount;
    public bool IsDisciplineEnabled => _punishment.Enabled;
    public bool ShowDisciplineProgress =>
        IsDisciplineEnabled && DisciplineTargetStrictnessIndex < AgentModeRegistry.MaxStrictnessIndex;

    private int DisciplineTargetStrictnessIndex =>
        Math.Max(_agentMode.BaseStrictnessIndex, _punishment.FloorLevelIndex);

    /// <summary>
    /// True once the effective mode is Sub or stricter. In Trusted Sub (index 0) time-limit
    /// and distraction enforcement is "soft": a reached limit drops trust + reminds instead
    /// of closing apps or locking the screen. Safety filtering stays hard in every mode.
    /// </summary>
    private bool IsHardDisciplineEnforcement => DisciplineTargetStrictnessIndex >= 1;

    public string DisciplineProgressText => BuildDisciplineProgressText();

    /// <summary>Current trust gauge, 0-100 (full = trusted). Drives the discipline progress bar.</summary>
    public double DisciplineProgressValue => _punishment.TrustValue;

    public int TrustValue => _punishment.TrustValue;

    public bool IsDisciplineEscalated => _punishment.FloorLevelIndex > 0;

    public string DisciplineModeStatus => BuildDisciplineModeStatus();

    // The status box only adds information beyond the progress text above when escalated or mid-countdown;
    // otherwise it would just repeat the same "X/Y toward Z" line a second time.
    public bool ShowDisciplineStatusBox => IsDisciplineEscalated || ShowPunishmentCountdown;

    public bool ShowInfractionsGood =>
        IsDisciplineEnabled && InfractionCount == 0 && !ShowPunishmentCountdown;

    public bool HasRecentInfractions => Infractions.Count > 0;

    public bool ShowPunishmentCountdown => _showPunishmentCountdown;
    public string PunishmentDeescalationLabel => UiCopy.PunishmentDeescalationLabel;

    public string PunishmentDeescalationCountdown
    {
        get => _punishmentDeescalationCountdown;
        private set => SetField(ref _punishmentDeescalationCountdown, value);
    }

    public string BedtimeLabel => _bedtime.BedtimeLabel;
    public string BedtimeCardLabel => _bedtime.BedtimeCardLabel;
    public bool IsBlueLightFilterActive => _bedtime.IsBlueLightFilterActive;

    public BlueLightFilterPhase BlueLightFilterPhase => _bedtime.BlueLightFilterPhase;
    public string StudyTimeLabel => _studyTime.StudyTimeLabel;
    public bool IsStudyModeActive => _studyTime.IsActiveNow;

    public string GamingUsedLabel => FormatDuration(_gaming.TotalUsage);
    public string GamingLimitLabel => FormatDuration(_gaming.LimitDuration);
    public string GamingRemainingLabel
    {
        get
        {
            var remaining = _gaming.LimitDuration - _gaming.TotalUsage;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            return FormatDuration(remaining);
        }
    }
    public double GamingProgress => _gaming.Progress;
    public string GamingUsageSummary =>
        _gaming.LimitMinutes <= 0
            ? UiCopy.PlayTimeTileZeroLimit
            : UiCopy.PlayTimeTileUsageFormat(GamingUsedLabel, GamingLimitLabel);

    public string YoutubeUsedLabel => FormatDuration(_youtube.TotalUsage);
    public string YoutubeLimitLabel => FormatDuration(_youtube.LimitDuration);
    public double YoutubeProgress => _youtube.Progress;
    public string YoutubeUsageSummary =>
        UiCopy.YoutubeTileUsageFormat(YoutubeUsedLabel, YoutubeLimitLabel);
    public string YoutubeDetailTotalLabel => UiCopy.YoutubeDetailTotalLabel;
    public double ScreenTimeLimitMinutes =>
        Config.TestingShortScreenTime ? 3 : _agentMode.ScreenTimeLimitMinutes;
    public double ScreenTimeUsedMinutes => _screenTime.Elapsed.TotalMinutes;

    public string UrlBlockingStatus =>
        UiCopy.UrlBlockingStatus(
            DomBlockedHosts().Count,
            _urlBlocking.IsServerRunning,
            _urlBlocking.LastHostsError,
            _urlBlocking.HasHostsOrphanEntries);

    public string SafeSearchStatus
    {
        get
        {
            if (!_safeSearch.HasAdminRights)
                return UiCopy.SafeSearchNoAdmin;

            if (_safeSearch.IsActive)
                return _safeSearch.LastError is null
                    ? UiCopy.SafeSearchActive
                    : $"{UiCopy.SafeSearchActive} ({_safeSearch.LastError})";

            return _safeSearch.LastError ?? UiCopy.SafeSearchInactive;
        }
    }

    public string YouTubeRestrictedModeStatus
    {
        get
        {
            if (!IsYouTubeRestrictedModeEnabled)
                return UiCopy.YouTubeRestrictedModeInactive;

            if (!_youtubeRestricted.HasAdminRights)
                return UiCopy.YouTubeRestrictedModeNoAdmin;

            if (_youtubeRestricted.IsActive)
                return _youtubeRestricted.LastError is null
                    ? UiCopy.YouTubeRestrictedModeActive
                    : $"{UiCopy.YouTubeRestrictedModeActive} ({_youtubeRestricted.LastError})";

            return _youtubeRestricted.LastError ?? UiCopy.YouTubeRestrictedModeInactive;
        }
    }

    private bool IsYouTubeRestrictedModeEnabled =>
        _localMode.IsEnabled
            ? _localSettings.Catalog.YouTubeRestrictedModeEnabled
            : _youtube.RestrictedModeEnabled;

    public string ImageShieldStatus =>
        UiCopy.ImageShieldStatus(
            _imageShield.HasAdminRights,
            ExtensionConfigResolver.IsReady,
            _imageShieldPolicy.PoliciesActive,
            ExtensionStoreConfigLoader.LastError ?? _imageShield.LastError);

    private void RefreshProtectionStatus()
    {
        OnPropertyChanged(nameof(UrlBlockingStatus));
        OnPropertyChanged(nameof(SafeSearchStatus));
        OnPropertyChanged(nameof(YouTubeRestrictedModeStatus));
        OnPropertyChanged(nameof(ImageShieldStatus));
        RefreshLocalImageShieldStatus();
    }

    public bool IsEnrolled
    {
        get => _isEnrolled;
        private set => SetField(ref _isEnrolled, value);
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
        if (SetField(ref _isConnecting, value))
        {
            EnrollCommand.RaiseCanExecuteChanged();
            ResetEnrollmentCommand.RaiseCanExecuteChanged();
            NotifySupervisionPresentationChanged();
        }
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (SetField(ref _isOnline, value))
            {
                OnPropertyChanged(nameof(OnlineLabel));
                NotifySupervisionPresentationChanged();
            }
        }
    }

    public bool IsScreenTimeLocked
    {
        get => _isScreenTimeLocked;
        private set => SetField(ref _isScreenTimeLocked, value);
    }

    public bool IsDomLocked
    {
        get => _isDomLocked;
        private set => SetField(ref _isDomLocked, value);
    }

    public bool IsBedtimeLocked
    {
        get => _isBedtimeLocked;
        private set => SetField(ref _isBedtimeLocked, value);
    }

    public bool IsLockOverlayVisible => IsScreenTimeLocked || IsDomLocked || IsBedtimeLocked;

    // Trusted Sub soft enforcement: warns about a time limit but the Sub can click through it
    // themselves — unlike the lock overlay above, no Dom PIN is required.
    public bool IsSoftLimitWarningVisible
    {
        get => _isSoftLimitWarningVisible;
        private set => SetField(ref _isSoftLimitWarningVisible, value);
    }

    public string SoftLimitWarningTitle => _softLimitWarningKind switch
    {
        SoftLimitKind.Youtube => UiCopy.SoftLimitWarningYoutubeTitle,
        SoftLimitKind.Bedtime => UiCopy.SoftLimitWarningBedtimeTitle,
        _ => UiCopy.SoftLimitWarningTitle,
    };

    public string SoftLimitWarningBody => _softLimitWarningKind switch
    {
        SoftLimitKind.Youtube => UiCopy.SoftLimitWarningYoutubeBody,
        SoftLimitKind.Bedtime => UiCopy.SoftLimitWarningBedtimeBody,
        _ => UiCopy.SoftLimitWarningBody,
    };

    public string SoftLimitWarningFooter => UiCopy.SoftLimitWarningFooter;
    public string SoftLimitWarningContinueLabel => UiCopy.SoftLimitWarningContinueButton;

    public bool ShowLockScreenTimeStats => IsScreenTimeLocked && !IsDomLocked && !IsBedtimeLocked;

    public bool ShowBedtimeCountdown => IsBedtimeLocked && !IsDomLocked;

    public string BedtimeCountdownLabel => UiCopy.BedtimeLockCountdownLabel;

    public string BedtimeUnlockCountdown
    {
        get => _bedtimeUnlockCountdown;
        private set => SetField(ref _bedtimeUnlockCountdown, value);
    }

    public bool ShowLockMoonIcon => IsBedtimeLocked && !IsDomLocked;

    public bool ShowLockMascot => ShowMascot && !ShowLockMoonIcon;
    public bool ShowLockMascotGuardi => ShowLockMascot && IsSubMode;
    public bool ShowLockMascotTrustedSub => ShowLockMascot && IsTrustedSubMode;
    public bool ShowLockMascotRestrictedSub => ShowLockMascot && IsRestrictedSubMode;
    public bool ShowMascotGuardi => ShowMascot && IsSubMode;
    public bool ShowMascotTrustedSub => ShowMascot && IsTrustedSubMode;
    public bool ShowMascotRestrictedSub => ShowMascot && IsRestrictedSubMode;

    public bool ShowLockDismissButton => IsLockOverlayVisible;

    public string LockOverlayHeadline =>
        IsDomLocked ? UiCopy.DomLockHeadline
        : IsBedtimeLocked ? UiCopy.BedtimeLockHeadline(SubDisplayName)
        : UiCopy.ScreenTimeLockHeadline(SubDisplayName);

    public string LockOverlayBody =>
        IsDomLocked ? UiCopy.DomLockBody
        : IsBedtimeLocked ? UiCopy.BedtimeLockBody
        : UiCopy.ScreenTimeLockBody;

    public string LockOverlayFooter =>
        IsDomLocked ? UiCopy.DomLockFooter
        : IsBedtimeLocked ? UiCopy.BedtimeLockFooter
        : UiCopy.ScreenTimeLockFooter;

    public string LockOverlayStatsLabel => UiCopy.ScreenTimeLockStatsLabel(_agentMode.DisplayName);

    public string LockOverlayDismissLabel =>
        IsDomLocked ? UiCopy.DomLockDismissLabel
        : IsBedtimeLocked ? UiCopy.BedtimeLockDismissLabel
        : UiCopy.ScreenTimeLockDismissLabel;

    public string LockOverlayDismissHint =>
        IsDomLocked ? UiCopy.DomLockDismissHint
        : IsBedtimeLocked ? UiCopy.BedtimeLockDismissHint
        : UiCopy.ScreenTimeLockDismissHint;

    public string OnlineLabel => IsOnline ? UiCopy.OnlineProtected : UiCopy.OnlineSyncing;

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string FocusedWindow
    {
        get => _focusedWindow;
        private set => SetField(ref _focusedWindow, value);
    }

    public int RunningAppsCount
    {
        get => _runningAppsCount;
        private set => SetField(ref _runningAppsCount, value);
    }

    public string LastDomMessage
    {
        get => _lastDomMessage;
        private set => SetField(ref _lastDomMessage, value);
    }

    public string EnrollmentCode
    {
        get => _enrollmentCode;
        set
        {
            if (SetField(ref _enrollmentCode, value))
            {
                EnrollCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanEnroll));
            }
        }
    }

    public string PcName
    {
        get => _pcName;
        set => SetField(ref _pcName, value);
    }

    public string EnrollmentDetail
    {
        get => _enrollmentDetail;
        private set => SetField(ref _enrollmentDetail, value);
    }

    public string ScreenTimeUsed
    {
        get => _screenTimeUsed;
        private set => SetField(ref _screenTimeUsed, value);
    }

    public string ScreenTimeLimit
    {
        get => _screenTimeLimit;
        private set => SetField(ref _screenTimeLimit, value);
    }

    public double ScreenTimeProgress
    {
        get => _screenTimeProgress;
        private set => SetField(ref _screenTimeProgress, value);
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set => SetField(ref _currentTime, value);
    }

    public bool CanEnroll =>
        !IsConnecting
        && !string.IsNullOrWhiteSpace(EnrollmentCode)
        && EnrollmentCode.Trim().Length == 8;

    public RelayCommand EnrollCommand { get; }
    public RelayCommand ResetEnrollmentCommand { get; }
    public RelayCommand<KioskAppItem> LaunchKioskAppCommand { get; }
    public RelayCommand RequestKioskExitCommand { get; }
    public RelayCommand NavigateToKioskAppsCommand { get; }
    public bool HasKioskApps => KioskApps.Count > 0;
    public bool IsKioskActive => _kioskPresentationActive;
    public bool ExitPinRequired => _exitPin.IsRequired;
    public RelayCommand DismissLockOverlayCommand { get; }
    public RelayCommand ContinueSoftLimitAnywayCommand { get; }
    public bool IsLocalMode => _localMode.IsEnabled;
    public bool IsLocalSyncing => _isLocalSyncing;
    public bool ShowEnrollmentOverlay => !IsEnrolled && !IsLocalMode;
    public string LocalModeButtonLabel => IsLocalMode ? LocalModeCopy.ExitButton : LocalModeCopy.EnterButton;
    public string LocalModeBannerTitle => LocalModeCopy.BannerTitle;
    public string LocalModeBannerBody => LocalModeCopy.BannerBody;

    public RelayCommand ToggleLocalModeCommand { get; }
    public RelayCommand NavigateToLocalSettingsCommand { get; }
    public RelayCommand<string> OpenLocalSectionCommand { get; }
    public RelayCommand SaveLocalSectionCommand { get; }
    public RelayCommand BlockLocalUrlCommand { get; }
    public RelayCommand BlockLocalAppCommand { get; }
    public RelayCommand LocalLockScreenCommand { get; }

    public RelayCommand<string> OpenRestrictionDetailCommand { get; }
    public RelayCommand NavigateBackCommand { get; }

    public bool IsHomePage => CurrentPage == DashboardPage.Home;

    public bool ShowBackButton => !IsHomePage;

    public bool IsKioskAppsPage => CurrentPage == DashboardPage.KioskApps;

    public bool IsDetailPage =>
        CurrentPage is DashboardPage.BlockedApps or DashboardPage.BlockedWebsites or DashboardPage.PlayTime or DashboardPage.YoutubeTime;

    public bool IsSettingsPage => IsDetailPage || IsLocalAreaPage;

    public DashboardPage CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetField(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsHomePage));
                OnPropertyChanged(nameof(ShowBackButton));
                OnPropertyChanged(nameof(IsKioskAppsPage));
                OnPropertyChanged(nameof(IsDetailPage));
                OnPropertyChanged(nameof(IsLocalHubPage));
                OnPropertyChanged(nameof(IsLocalSectionPage));
                OnPropertyChanged(nameof(IsLocalAreaPage));
                OnPropertyChanged(nameof(IsSettingsPage));
                OnPropertyChanged(nameof(HeaderCenterTitle));
                OnPropertyChanged(nameof(HeaderCenterSubtitle));
                OnPropertyChanged(nameof(HomeGreetingTitle));
                OnPropertyChanged(nameof(HomeGreetingSubtitle));
                OnPropertyChanged(nameof(IsBlockedListDetail));
                OnPropertyChanged(nameof(IsPlayTimeDetail));
                OnPropertyChanged(nameof(IsYoutubeTimeDetail));
                OnPropertyChanged(nameof(DetailPageTitle));
                OnPropertyChanged(nameof(DetailPageSubtitle));
                OnPropertyChanged(nameof(DetailEmptyMessage));
                NavigateBackCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBlockedListDetail =>
        CurrentPage is DashboardPage.BlockedApps or DashboardPage.BlockedWebsites;

    public bool IsPlayTimeDetail => CurrentPage == DashboardPage.PlayTime;

    public bool IsYoutubeTimeDetail => CurrentPage == DashboardPage.YoutubeTime;

    public string DetailPageTitle => CurrentPage switch
    {
        DashboardPage.BlockedApps => UiCopy.DetailBlockedAppsTitle,
        DashboardPage.BlockedWebsites => UiCopy.DetailBlockedSitesTitle,
        DashboardPage.PlayTime => UiCopy.PlayTimeDetailTitle,
        DashboardPage.YoutubeTime => UiCopy.YoutubeDetailTitle,
        _ => string.Empty,
    };

    public string DetailPageSubtitle => CurrentPage switch
    {
        DashboardPage.BlockedApps when DetailItems.Count == 0 => UiCopy.DetailAppsEmptySubtitle,
        DashboardPage.BlockedApps => UiCopy.DetailAppsCountFormat(DetailItems.Count),
        DashboardPage.BlockedWebsites when DetailItems.Count == 0 => UiCopy.DetailSitesEmptySubtitle,
        DashboardPage.BlockedWebsites => UiCopy.DetailSitesCountFormat(DetailItems.Count),
        DashboardPage.PlayTime => _gaming.LimitMinutes <= 0
            ? UiCopy.PlayTimeDetailZeroLimit
            : UiCopy.PlayTimeDetailSubtitleFormat(GamingUsedLabel, GamingLimitLabel),
        DashboardPage.YoutubeTime => UiCopy.YoutubeDetailSubtitleFormat(YoutubeUsedLabel, YoutubeLimitLabel),
        _ => string.Empty,
    };

    public string DetailEmptyMessage => CurrentPage switch
    {
        DashboardPage.BlockedApps => UiCopy.DetailAppsEmpty,
        DashboardPage.BlockedWebsites => UiCopy.DetailSitesEmpty,
        DashboardPage.PlayTime => _gaming.LimitMinutes <= 0
            ? UiCopy.PlayTimeDetailZeroLimit
            : UiCopy.PlayTimeDetailEmptyWithLimitFormat(GamingRemainingLabel, GamingLimitLabel),
        DashboardPage.YoutubeTime => UiCopy.YoutubeDetailEmpty,
        _ => string.Empty,
    };

    public void Dispose()
    {
        BrowserInstallOrchestrator.RestartCountdownHandler = null;
        if (_disposed)
            return;

        _disposed = true;
        _startupCts?.Cancel();

        if (!_coreShutdownDone)
            ShutdownCore();

        _startupCts?.Dispose();
        _startupCts = null;
        _screenTime.Dispose();
        _gaming.Dispose();
        _appTimeLimits.Dispose();
        _youtube.Dispose();
        _punishment.Dispose();
        _extensionInfractionWatcher.Dispose();
        _extensionInfractionHttp.Dispose();
        _studyTime.Dispose();
        _studyDistractionGuard.Dispose();

        _bedtime.Dispose();
        StopBedtimeCountdownTimer();
        StopBedtimeOverrunTimer();
        StopPunishmentCountdownTimer();
        StopWidgetPromptTimer();
        _safeSearch.Dispose();
        _youtubeRestricted.Dispose();
        _imageShield.Dispose();
        _extensionGuard.Dispose();
        _extensionPolicyWatchdog.Dispose();
        _kiosk.Dispose();
        _agentMode.Dispose();
        _host.Dispose();
    }

    public void PrepareSession()
    {
        NativeMessagingHostRegistry.Register();
        // Release browser policies left by a crash/kill before anything re-applies them.
        _safeSearch.TryReleaseOrphanedState();
        _youtubeRestricted.TryReleaseOrphanedState();
        _imageShield.TryReleaseOrphanedState();
        _imageShield.TryMigrateStalePolicies();

        NavigateHome();
        _host.TryLoadSavedEnrollment(this);
        ReconcileAutoStartTask();
        if (_localMode.IsEnabled)
        {
            _localSettings.EnsureExitPinSeeded();
            BootstrapLocalSupervision();
            _localSettings.ApplyActiveMode();
            OnPropertyChanged(nameof(ExitPinRequired));
            StatusText = LocalModeCopy.StatusLabel;
            if (!ExitPinRequired && IsEnrolled)
                _ = TryRepairMissingExitPinAsync();
        }

        NotifySupervisionPresentationChanged();
    }

    /// <summary>
    /// First-run gate: local-vs-online choice, name, and (local only) exit PIN — all in one
    /// wizard, always Trusted Sub themed. Local mode is enabled directly here rather than via
    /// <see cref="ToggleLocalModeCommand"/>, since that command's PIN-authorization gate would
    /// immediately re-prompt for the PIN the Sub just finished setting two steps earlier.
    /// </summary>
    public bool EnsureOnboarded(Window owner)
    {
        if (_subProfile.HasDisplayName)
            return true;

        var dialog = new WelcomeWizardWindow(_subProfile, _exitPin) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return false;

        if (dialog.ChoseLocalSetup && !IsLocalMode)
        {
            _localSettings.SeedFromRuntimeIfEmpty();
            _localMode.Enable();
            AddLog(LocalModeCopy.EnabledLog);
        }

        RefreshSubPersonalization();
        return true;
    }

    /// <summary>
    /// Mandatory, one-time tour shown right after onboarding completes. No-ops on every
    /// later launch once seen — gated by its own persisted flag, independent of
    /// <see cref="EnsureOnboarded"/>, so it's safe to call unconditionally after it.
    /// </summary>
    public void EnsureWelcomeTourShown(Window owner)
    {
        if (_agentPreferences.WelcomeTourSeen)
            return;

        new WelcomeTourWindow { Owner = owner }.ShowDialog();

        // The tour re-themes the window per page to preview each mode; restore the actual
        // effective mode's theme now that it's closed.
        ThemeService.Apply(_agentMode.Theme, _agentMode.Ui);

        _agentPreferences.WelcomeTourSeen = true;
        _agentPreferencesStore.Save(_agentPreferences);
    }

    private void OnSubProfileChanged() => PostOnUi(RefreshSubPersonalization);

    private void RefreshSubPersonalization()
    {
        OnPropertyChanged(nameof(SubDisplayName));
        OnPropertyChanged(nameof(HasSubDisplayName));
        OnPropertyChanged(nameof(PersonalizedName));
        OnPropertyChanged(nameof(HeaderCenterTitle));
        OnPropertyChanged(nameof(HeaderCenterSubtitle));
        OnPropertyChanged(nameof(HomeGreetingTitle));
        OnPropertyChanged(nameof(HomeGreetingSubtitle));
        OnPropertyChanged(nameof(StandingLabel));
        OnPropertyChanged(nameof(MascotGreeting));
        OnPropertyChanged(nameof(LockOverlayHeadline));
        OnPropertyChanged(nameof(WidgetToolTip));
        LockOverlayChanged?.Invoke();
    }

    private async Task TryRepairMissingExitPinAsync()
    {
        if (!IsLocalMode || ExitPinRequired || !IsEnrolled)
            return;

        var restored = await _host.TryRestoreExitPinFromServerAsync(CancellationToken.None).ConfigureAwait(false);
        if (!restored)
            return;

        PostOnUi(() =>
        {
            _localSettings.EnsureExitPinSeeded();
            _localSettings.Persist();
            OnPropertyChanged(nameof(ExitPinRequired));
            AddLog("Exit PIN restored from the web dashboard.");
        });
    }

    public async Task FinishInitializeAsync()
    {
        _startupCts?.Cancel();
        _startupCts?.Dispose();
        _startupCts = new CancellationTokenSource();
        var ct = _startupCts.Token;

        try
        {
            await _host.DiscoverCapabilitiesAsync(this, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            ExtensionStoreConfigLoader.Initialize(log: msg => PostOnUi(() => AddLog(msg)));
            ct.ThrowIfCancellationRequested();

            await Task.Run(() => ApplyProtectionPolicies(ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                    return;

                RefreshProtectionStatus();

                if (_safeSearch.IsActive)
                    AddLog($"{UiCopy.MascotName} locked SafeSearch in your browsers (restart them if already open).");
                else
                    AddLog(_safeSearch.LastError ?? UiCopy.SafeSearchInactive);

                ReconcileYoutubeRestrictedModeCore();

                if (_imageShield.IsActive)
                    AddLog($"{UiCopy.MascotName} locked the image shield policy in your browsers.");
                else if (ExtensionConfigResolver.IsReady)
                    AddLog(_imageShield.LastError ?? "Image shield could not be applied.");
                else
                    AddLog(ExtensionStoreConfigLoader.LastError ?? "Image shield not configured yet.");

                RefreshProtectionStatus();
            }, DispatcherPriority.Background, ct);
        }
        catch (OperationCanceledException)
        {
            // Startup aborted because the app is closing.
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
        {
            try
            {
                await _dispatcher.InvokeAsync(() => AddLog($"Startup error: {ex.Message}"));
            }
            catch (TaskCanceledException)
            {
                // App closed while reporting the startup error.
            }
        }
    }

    private void ApplyProtectionPolicies(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _safeSearch.Apply();

        ct.ThrowIfCancellationRequested();
        ReconcileYoutubeRestrictedModeCore(ct);

        // After SafeSearch so the Firefox policies.json merge sees the base file.
        ct.ThrowIfCancellationRequested();
        ReconcileImageShieldCore(ct);

        ct.ThrowIfCancellationRequested();
        SyncFirefoxDevOnStartup(ct);
        SyncFirefoxStoreOnStartup(ct);
        SyncChromiumOnStartup(ct);

        ct.ThrowIfCancellationRequested();
        _urlBlocking.ReconcileFromDisk();
        _webContentFilter.LoadAndApply();

        if (_host.IsEnrolled)
        {
            ct.ThrowIfCancellationRequested();
            _host.StartServices();
            if (_urlBlocking.BlockedHosts.Count > 0 && !_urlBlocking.IsServerRunning)
                _urlBlocking.Apply();
        }

        ct.ThrowIfCancellationRequested();
        SyncVpnShield();
    }

    private void SyncFirefoxDevOnStartup(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!Config.ExtensionGuardFirefoxLocalMode || !_imageShield.IsConfigured)
            return;

        if (!_imageShieldPolicy.IsEffectivelyEnabled(_agentMode.Slug))
            return;

        var settings = _imageShieldPolicy.BuildTuningSettings(_agentMode.Slug);
        _imageShield.TrySyncFirefoxDevOnStartup(
            settings,
            log: msg => PostOnUi(() => AddLog(msg)),
            onRestarting: (headline, body) => PostOnUi(() =>
                FirefoxRestartToastRequested?.Invoke(headline, body)));
    }

    private void SyncFirefoxStoreOnStartup(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Config.ExtensionGuardFirefoxLocalMode || !_imageShield.IsConfigured)
            return;

        _imageShield.TrySyncFirefoxStoreOnStartup(
            log: msg => PostOnUi(() => AddLog(msg)),
            onRestarting: (headline, body) => PostOnUi(() =>
                FirefoxRestartToastRequested?.Invoke(headline, body)));
    }

    private void SyncChromiumOnStartup(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!Config.ExtensionGuardEnforceChromium || !_imageShield.IsConfigured)
            return;

        if (!_imageShieldPolicy.IsEffectivelyEnabled(_agentMode.Slug))
            return;

        var settings = _imageShieldPolicy.BuildTuningSettings(_agentMode.Slug);
        _imageShield.TrySyncChromiumOnStartup(
            settings,
            log: msg => PostOnUi(() => AddLog(msg)),
            onRestarting: (headline, body) => PostOnUi(() =>
                FirefoxRestartToastRequested?.Invoke(headline, body)));
    }

    private Models.AgentShieldStateDto BuildAgentShieldState()
    {
        var displayName = _subProfile.DisplayName;
        var slug = _agentMode.Slug;
        Models.AgentShieldStateDto state;
        if (!_imageShieldPolicy.IsEffectivelyEnabled(slug)
            || !_imageShieldPolicy.PoliciesActive
            || !_imageShield.HasPersistedInstall
            || !ImageShieldRuntimeStore.IsFilteringActive)
        {
            state = AgentShieldStateFactory.WithDisplayName(Models.AgentShieldStateDto.Inactive, displayName);
        }
        else
        {
            var settings = _imageShieldPolicy.BuildTuningSettings(slug);
            var browserActive = _imageShieldPolicy.BuildBrowserActiveMap(slug);
            state = AgentShieldStateFactory.FromSettings(true, settings, browserActive, displayName);
        }

        return ApplyYoutubeAccessState(state);
    }

    private Models.AgentShieldStateDto ApplyYoutubeAccessState(Models.AgentShieldStateDto state)
    {
        var managed = new Dictionary<string, object>(state.Managed, StringComparer.Ordinal)
        {
            ["youtubeBlocked"] = _youtube.ShouldBlockYoutubeAccess,
            ["youtubeSoftLimit"] = _youtube.ShouldSoftWarnYoutubeAccess && !_youtubeSoftLimitWarningBypassed,
        };

        var reason = _youtube.YoutubeBlockReason;
        if (!string.IsNullOrEmpty(reason))
            managed["youtubeBlockReason"] = reason;

        // Appearance (global): let the extension hide its own visible surfaces without
        // changing enforcement. The blur/filtering still runs; only the chrome is hidden.
        managed["styledNewTab"] = _localSettings.Catalog.StyledNewTabPage;
        managed["websiteBadge"] = _localSettings.Catalog.ShowWebsiteBadge;

        // Web-content category keywords for the extension's hostname heuristic (catches
        // sites not in the curated hosts-file list). Curated domains are already blocked
        // at DNS level, so only the keyword tokens need to travel to the browser.
        var categoryKeywords = _webContentFilter.EnabledKeywords;
        if (categoryKeywords.Count > 0)
            managed["blockedCategoryKeywords"] = categoryKeywords;

        // Weighted page-text vocabularies for the extension's on-device content scoring
        // (catches exotic sites that neither the curated lists nor the DNS filter know).
        var categoryContent = _webContentFilter.EnabledContentTerms;
        if (categoryContent.Count > 0)
            managed["blockedCategoryContent"] = categoryContent;

        return state with { AgentRunning = true, Managed = managed };
    }

    public void ShutdownCore()
    {
        if (_coreShutdownDone)
            return;

        ProcessGuardian.SignalIntentionalShutdown();

        DeactivateImageShieldForShutdown();
        _startupCts?.Cancel();
        _host.StopLoopAndWait(TimeSpan.FromSeconds(1));
        _extensionGuard.Stop();
        ExtensionHostServer.StopShared();
        _safeSearch.Release();
        _youtubeRestricted.Release();
        _urlBlocking.ReleaseBlocking();
        _webContentFilter.ReleaseEnforcement();
        _vpnBlocking.Disable(_sessionState, _urlBlocking, applyUrlChanges: false);
        WaitForExtensionShutdownSignal();
        EmergencyReleaseKiosk();
        _coreShutdownDone = true;
    }

    private void DeactivateImageShieldForShutdown()
    {
        ImageShieldRuntimeStore.SetFilteringActive(false);

        try
        {
            var offSettings = _imageShieldPolicy.BuildTuningSettings(_agentMode.Slug) with { ShieldActive = false };
            _imageShield.SetRuntimeActive(false, offSettings);
            _imageShieldPolicy.SetPoliciesActive(false);
            AuditLog.Write("Image shield shutdown — runtime inactive, HTTP bridge stays up briefly for extension poll.");
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Image shield shutdown deactivate failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Keep /shield-state reachable long enough for the extension poll loop (1s) to observe inactive.
    /// </summary>
    private static void WaitForExtensionShutdownSignal()
    {
        Thread.Sleep(TimeSpan.FromSeconds(4));
    }

    /// <summary>Restores taskbar/keyboard and exits kiosk presentation after errors or shutdown.</summary>
    public void EmergencyReleaseKiosk()
    {
        try
        {
            _kiosk.ForceRelease();
        }
        catch (Exception ex)
        {
            AuditLog.Write($"EmergencyReleaseKiosk failed: {ex.Message}");
        }

        if (!_kioskPresentationActive)
            return;

        _kioskPresentationActive = false;
        PostOnUi(() =>
        {
            try
            {
                OnPropertyChanged(nameof(IsKioskActive));
                NavigateToKioskAppsCommand.RaiseCanExecuteChanged();
                KioskStateChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                AuditLog.Write($"EmergencyReleaseKiosk UI notify failed: {ex}");
            }
        });
    }

    public void Shutdown()
    {
        ShutdownCore();

        if (_disposed)
            return;

        PostOnUi(() =>
        {
            Log($"{UiCopy.MascotName} turned off website shields and SafeSearch.");
            RefreshRestrictions();
            OnPropertyChanged(nameof(UrlBlockingStatus));
            OnPropertyChanged(nameof(SafeSearchStatus));
        });
    }

    public void Log(string message)
    {
        if (_dispatcher.CheckAccess())
            AddLog(message);
        else
            _dispatcher.BeginInvoke(() => AddLog(message));
    }

    public void HeartbeatUpdated(HeartbeatRequest status, bool success)
    {
        PostOnUi(() =>
        {
            FocusedWindow = status.FocusedWindow;
            RunningAppsCount = status.RunningApps.Count;
            if (IsLocalMode)
            {
                IsOnline = success;
                StatusText = LocalModeCopy.StatusLabel;
            }
            else
            {
                IsOnline = success;
                StatusText = success ? UiCopy.StatusSupervisionActive : UiCopy.StatusConnectionIssue;
            }

            OnPropertyChanged(nameof(RunningAppsLabel));
            NotifySupervisionPresentationChanged();
        });
    }

    public void CommandExecuted(string type, bool success) =>
        PostOnUi(() => AddLog($"{UiCopy.MascotName} ran safety command {type} → {(success ? "OK" : "failed")}"));

    public void DomMessageReceived(string text) =>
        PostOnUi(() =>
        {
            if (_localMode.IsEnabled)
                return;

            LastDomMessage = text;
            DomMessagePopupRequested?.Invoke(text);
        });

    public void AppClosedByGuardi(string processName, AppBlockCategory category)
    {
        var displayName = AppDisplayNames.Resolve(processName);
        var (title, message) = AppBlockMessages.Format(category, displayName);

        RegisterAppBlockInfraction(processName, displayName, category);

        PostOnUi(() =>
        {
            AddLog($"{UiCopy.MascotName} closed {displayName}.");
            AppBlockedPopupRequested?.Invoke(title, message);
        });
    }

    private void RegisterAppBlockInfraction(string processName, string displayName, AppBlockCategory category)
    {
        switch (category)
        {
            case AppBlockCategory.VpnShield:
                _punishment.RegisterInfraction(
                    InfractionKind.VpnAttempt,
                    $"vpn:{processName}",
                    string.Format(UiCopy.InfractionVpnDetail, displayName));
                break;
            case AppBlockCategory.StudyTime:
                if (_studyTime.IsInActivationGracePeriod)
                    break;

                _punishment.RegisterInfraction(
                    InfractionKind.StudyTimeViolation,
                    $"study:{processName}",
                    string.Format(UiCopy.InfractionStudyDetail, displayName));
                break;
            case AppBlockCategory.DomManual:
            case AppBlockCategory.DomImmediate:
            case AppBlockCategory.ModeRule:
                _punishment.RegisterInfraction(
                    InfractionKind.BlockedAppRepeated,
                    processName,
                    string.Format(UiCopy.InfractionBlockedAppDetail, displayName));
                break;
        }
    }

    public void BedtimeSettingsReceived(BedtimeSettingsPayload payload)
    {
        if (_localMode.IsEnabled)
            return;

        var settings = BedtimeSettings.FromPayload(
            payload.Enabled,
            payload.Time,
            payload.WakeTime,
            _bedtime.Settings,
            payload.Weekly,
            payload.BlueLightFilterEnabled);

        PostOnUi(() =>
        {
            if (_bedtime.Settings.ScheduleKey == settings.ScheduleKey)
                return;

            _bedtime.Update(settings);
            OnPropertyChanged(nameof(BedtimeLabel));
            OnPropertyChanged(nameof(BedtimeCardLabel));
            AddLog($"{UiCopy.MascotName} updated sleepy time to {settings.DisplayLabel}.");
        });
    }

    public void ExitPinReceived(string? pin)
    {
        if (_localMode.IsEnabled)
            return;

        _exitPin.UpdateFromServer(pin);
        PostOnUi(() => OnPropertyChanged(nameof(ExitPinRequired)));
    }

    public bool TryAuthorizeQuit() => TryAuthorizeProtectedAction("quit");

    private bool TryAuthorizeProtectedAction(string context)
    {
        if (!_exitPin.IsRequired)
            return true;

        var authorized = false;
        _dispatcher.Invoke(() =>
        {
            var owner = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsVisible && w is LockOverlayWindow)
                ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                ?? Application.Current.MainWindow;

            var dialog = new ExitPinPromptWindow(_exitPin, context)
            {
                Owner = owner,
                Topmost = true,
            };
            authorized = dialog.ShowDialog() == true;
        });

        if (!authorized)
        {
            _punishment.RegisterInfraction(
                InfractionKind.BypassAttempt,
                $"pin:{context}",
                string.Format(UiCopy.InfractionBypassPinDetail, context));
        }

        return authorized;
    }

    public void EnrollmentChanged(bool enrolled, string? detail = null)
    {
        PostOnUi(() =>
        {
            IsEnrolled = enrolled;
            if (!string.IsNullOrWhiteSpace(detail))
                EnrollmentDetail = detail;

            if (enrolled)
            {
                EnsureAutoStartDefaultWhenSupervised();
                Task.Run(Security.SecurityActivation.EnsureActive);
                IsOnline = false;
                StatusText = IsLocalMode ? LocalModeCopy.StatusLabel : UiCopy.StatusStarting;
                _screenTimeLockBypassed = false;
                _softLimitWarningBypassed = false;
                _youtubeSoftLimitWarningBypassed = false;
                _gamingSoftLimitWarningBypassed = false;
                _gamingNagTickCount = 0;
                _youtubeNagTickCount = 0;
                _bedtimeLockBypassed = false;
                _kioskBypassed = false;
                _screenTime.Start();
                _gaming.Start();
                if (IsLocalMode)
                    _appTimeLimits.Start();
                _youtube.Start();
                _punishment.Start();
                ReconcileImageShield();
                _bedtime.SyncLockState();
                AddLog(detail ?? $"{UiCopy.MascotName} is protecting this computer now.");
            }
            else if (!IsLocalMode)
            {
                IsOnline = false;
                StatusText = UiCopy.StatusOffline;
                _screenTime.Stop();
                _gaming.Stop();
                _appTimeLimits.Stop();
                _youtube.Stop();
                _punishment.Stop();
                _extensionGuard.Stop();
                IsScreenTimeLocked = false;
                IsDomLocked = false;
                IsBedtimeLocked = false;
                _bedtimeLockBypassed = false;
                NotifyLockOverlayChanged();
                _vpnBlocking.Disable(_sessionState, _urlBlocking);
            }

            EnrollCommand.RaiseCanExecuteChanged();
            ResetEnrollmentCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanEnroll));
            OnPropertyChanged(nameof(ShowEnrollmentOverlay));
            SyncKioskState();
            NotifyWidgetScreenTimeChanged();
            NotifySupervisionPresentationChanged();
        });
    }

    private void NotifyWidgetScreenTimeChanged()
    {
        OnPropertyChanged(nameof(ShowWidgetScreenTime));
        OnPropertyChanged(nameof(WidgetScreenTimeRemaining));
        OnPropertyChanged(nameof(WidgetScreenTimeExhausted));
        OnPropertyChanged(nameof(DesktopWidgetVisible));
        NotifySupervisionPresentationChanged();
    }

    public void SessionRevoked()
    {
        PostOnUi(() =>
        {
            IsEnrolled = false;
            IsOnline = false;
            IsScreenTimeLocked = false;
            IsDomLocked = false;
            IsBedtimeLocked = false;
            _screenTimeLockBypassed = false;
            _softLimitWarningBypassed = false;
            _youtubeSoftLimitWarningBypassed = false;
            _gamingSoftLimitWarningBypassed = false;
            _bedtimeLockBypassed = false;
            _bedtimeSoftWarningBypassed = false;
            StopBedtimeOverrunTimer();
            StatusText = UiCopy.StatusSessionEnded;
            EnrollmentDetail = "Your Dom unlinked this computer. Ask for a new code to feel safe again.";
            _screenTime.Stop();
            _gaming.Stop();
            _appTimeLimits.Stop();
            _youtube.Stop();
            _punishment.Stop();
            _extensionGuard.Stop();
            NotifyLockOverlayChanged();
            _vpnBlocking.Disable(_sessionState, _urlBlocking);
            EnrollCommand.RaiseCanExecuteChanged();
            ResetEnrollmentCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanEnroll));
            SyncKioskState();
        });
    }

    public void RestrictionsChanged() => PostOnUi(() =>
    {
        RefreshRestrictions();
        OnPropertyChanged(nameof(UrlBlockingStatus));
    });

    public void ScreenTimeLimitReached()
    {
        _punishment.RegisterInfraction(
            InfractionKind.LimitIgnored,
            "screen_time",
            UiCopy.InfractionScreenTimeDetail);

        // Trusted Sub is soft: no lock screen, but a fullscreen warning still calls it out —
        // the Sub can click through it themselves (discouraged), unlike the hard lock below.
        if (!IsHardDisciplineEnforcement)
        {
            PostOnUi(() =>
            {
                AddLog(UiCopy.ScreenTimeSoftReminderLog);
                ShowSoftLimitWarning(SoftLimitKind.ScreenTime);
            });
            return;
        }

        PostOnUi(() =>
        {
            IsScreenTimeLocked = true;
            DismissLockOverlayCommand.RaiseCanExecuteChanged();
            NotifyLockOverlayChanged();
            ScreenTimeLocked?.Invoke();
        });
    }

    private void ShowSoftLimitWarning(SoftLimitKind kind)
    {
        if (IsSoftLimitWarningVisible)
            return;

        _softLimitWarningKind = kind;
        OnPropertyChanged(nameof(SoftLimitWarningTitle));
        OnPropertyChanged(nameof(SoftLimitWarningBody));
        IsSoftLimitWarningVisible = true;
        SoftLimitWarningRequested?.Invoke();
    }

    public void DomLockRequested()
    {
        PostOnUi(() =>
        {
            IsDomLocked = true;
            DismissLockOverlayCommand.RaiseCanExecuteChanged();
            NotifyLockOverlayChanged();
            AddLog($"{UiCopy.MascotName} locked the screen — your Dom said time-out.");
        });
    }

    public void DomLockReleased()
    {
        PostOnUi(() =>
        {
            if (!IsDomLocked)
                return;

            IsDomLocked = false;
            DismissLockOverlayCommand.RaiseCanExecuteChanged();
            NotifyLockOverlayChanged();
            AddLog($"{UiCopy.MascotName} unlocked the screen — your Dom let you back in.");
        });
    }

    private void DismissLockOverlay()
    {
        if (!TryAuthorizeProtectedAction("dismiss_lock"))
            return;

        if (IsDomLocked)
        {
            IsDomLocked = false;
            AddLog("Dom lock bypassed with Dom PIN.");
        }
        else if (IsBedtimeLocked)
        {
            _bedtimeLockBypassed = true;
            IsBedtimeLocked = false;
            StopBedtimeCountdownTimer();
            AddLog("Bedtime lock bypassed with Dom PIN.");
            ScreenTimeLockDismissed?.Invoke();
        }
        else if (IsScreenTimeLocked)
        {
            _screenTimeLockBypassed = true;
            IsScreenTimeLocked = false;
            AddLog("Screen time lock bypassed with Dom PIN.");
            ScreenTimeLockDismissed?.Invoke();
        }

        DismissLockOverlayCommand.RaiseCanExecuteChanged();
        NotifyLockOverlayChanged();
    }

    private void ContinueSoftLimitAnyway()
    {
        if (!IsSoftLimitWarningVisible)
            return;

        // Stays dismissed for the rest of today's session — otherwise the next elapsed-time
        // tick would just re-trigger the same warning immediately.
        if (_softLimitWarningKind == SoftLimitKind.Youtube)
        {
            _youtubeSoftLimitWarningBypassed = true;
            AddLog("Kept watching YouTube past the daily limit — Guardi's trust dipped a little.");
        }
        else if (_softLimitWarningKind == SoftLimitKind.Bedtime)
        {
            _bedtimeSoftWarningBypassed = true;
            StartBedtimeOverrunTimer();
            AddLog("Stayed up past bedtime — trust will keep draining while the computer stays on.");
        }
        else
        {
            _softLimitWarningBypassed = true;
            AddLog("Kept going past the screen time limit — Guardi's trust dipped a little.");
        }

        IsSoftLimitWarningVisible = false;
        SoftLimitWarningDismissed?.Invoke();
    }

    public event Action<ExtensionGuardState?>? ExtensionGuardStateChanged;
    public event Action<string, string>? FirefoxRestartToastRequested;
    public event Action<string, string, int>? BrowserRestartCountdownRequested;

    public void ForceExtensionGuardCheck() => _extensionGuard.ForceCheck();

    public void DismissExtensionGuardOverlay()
    {
        if (!Config.ExtensionGuardDevBypass)
            return;

        _extensionGuard.DismissOverlay();
    }

    private void OnExtensionGuardStateChanged(ExtensionGuardState? state) =>
        PostOnUi(() => ExtensionGuardStateChanged?.Invoke(state));

    private void OnUnsupportedBrowserBlocked(string displayName) =>
        PostOnUi(() =>
        {
            if (string.Equals(displayName, "Mozilla Firefox", StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"{UiCopy.MascotName} closed Mozilla Firefox — use Firefox Developer Edition.");
                AppBlockedPopupRequested?.Invoke(
                    ExtensionGuardCopy.FirefoxReleaseBlockedTitle,
                    ExtensionGuardCopy.FirefoxReleaseBlockedMessage);
                return;
            }

            AddLog($"{UiCopy.MascotName} closed {displayName} — only Guardi-protected browsers are allowed.");
            AppBlockedPopupRequested?.Invoke(
                $"{displayName} is not allowed",
                $"{UiCopy.MascotName} can't put his shield on {displayName}, so it stays closed. Use a browser Guardi protects, okay? \U0001F499");
        });

    private void NotifyLockOverlayChanged()
    {
        OnPropertyChanged(nameof(IsLockOverlayVisible));
        OnPropertyChanged(nameof(ShowLockScreenTimeStats));
        OnPropertyChanged(nameof(ShowBedtimeCountdown));
        OnPropertyChanged(nameof(BedtimeCountdownLabel));
        OnPropertyChanged(nameof(ShowLockDismissButton));
        OnPropertyChanged(nameof(ShowLockMoonIcon));
        OnPropertyChanged(nameof(ShowLockMascot));
        OnPropertyChanged(nameof(ShowLockMascotGuardi));
        OnPropertyChanged(nameof(ShowLockMascotTrustedSub));
        OnPropertyChanged(nameof(ShowLockMascotRestrictedSub));
        OnPropertyChanged(nameof(LockOverlayHeadline));
        OnPropertyChanged(nameof(LockOverlayBody));
        OnPropertyChanged(nameof(LockOverlayFooter));
        OnPropertyChanged(nameof(LockOverlayDismissLabel));
        OnPropertyChanged(nameof(LockOverlayDismissHint));
        LockOverlayChanged?.Invoke();
    }

    private void OnBedtimeWarningDue(BedtimeWarningKind kind)
    {
        var (title, message) = BedtimeMessages.Warning(kind);
        PostOnUi(() =>
        {
            AddLog($"{UiCopy.MascotName} bedtime reminder: {title}");
            BedtimeWarningPopupRequested?.Invoke(title, message);
        });
    }

    private void OnBlueLightFilterActiveChanged(bool active, BlueLightFilterPhase phase) =>
        PostOnUi(() => BlueLightFilterStateChanged?.Invoke(active, phase));

    private void OnBedtimeReached()
    {
        PostOnUi(() =>
        {
            if (!IsEnrolled || _bedtimeLockBypassed || IsBedtimeLocked)
                return;

            // Trusted Sub: soft warning instead of hard lock.
            if (!IsHardDisciplineEnforcement)
            {
                if (_bedtimeSoftWarningBypassed)
                    return;

                _punishment.RegisterInfraction(InfractionKind.LimitIgnored, "bedtime", "Stayed up past bedtime");
                AddLog($"It's past bedtime — {UiCopy.MascotName} is gently reminding you to rest.");
                ShowSoftLimitWarning(SoftLimitKind.Bedtime);
                return;
            }

            IsBedtimeLocked = true;
            StartBedtimeCountdownTimer();
            DismissLockOverlayCommand.RaiseCanExecuteChanged();
            NotifyLockOverlayChanged();
            ScreenTimeLocked?.Invoke();
            AddLog($"{UiCopy.MascotName} tucked the computer in for bedtime.");
        });
    }

    private void OnWakeTimeReached()
    {
        PostOnUi(() =>
        {
            _bedtimeLockBypassed = false;
            _bedtimeSoftWarningBypassed = false;
            StopBedtimeOverrunTimer();

            if (!IsBedtimeLocked)
                return;

            IsBedtimeLocked = false;
            StopBedtimeCountdownTimer();
            DismissLockOverlayCommand.RaiseCanExecuteChanged();
            NotifyLockOverlayChanged();
            ScreenTimeLockDismissed?.Invoke();
            AddLog($"{UiCopy.MascotName} woke the computer up — good morning, little one!");
        });
    }

    private void OnBedtimeSettingsChanged() =>
        PostOnUi(() =>
        {
            OnPropertyChanged(nameof(BedtimeLabel));
            OnPropertyChanged(nameof(BedtimeCardLabel));
            RefreshTodayRules();
        });

    private void StartBedtimeCountdownTimer()
    {
        _bedtimeCountdownTimer ??= new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateBedtimeCountdown(),
            _dispatcher);

        UpdateBedtimeCountdown();
        _bedtimeCountdownTimer.Start();
    }

    private void StopBedtimeCountdownTimer() =>
        _bedtimeCountdownTimer?.Stop();

    private void StartBedtimeOverrunTimer()
    {
        StopBedtimeOverrunTimer();
        _bedtimeOverrunTimer = new DispatcherTimer(
            TimeSpan.FromMinutes(10),
            DispatcherPriority.Background,
            OnBedtimeOverrunTick,
            _dispatcher);
        _bedtimeOverrunTimer.Start();
    }

    private void StopBedtimeOverrunTimer()
    {
        _bedtimeOverrunTimer?.Stop();
        _bedtimeOverrunTimer = null;
    }

    private void OnBedtimeOverrunTick(object? sender, EventArgs e)
    {
        if (!IsEnrolled || !_bedtimeSoftWarningBypassed)
            return;

        _punishment.RegisterInfraction(InfractionKind.LimitIgnored, "bedtime_overrun",
            "Still up past bedtime");
        AddLog($"Still up past bedtime — {UiCopy.MascotName} keeps losing a little trust.");
    }

    private void UpdateBedtimeCountdown()
    {
        var remaining = _bedtime.Settings.TimeUntilWake(DateTime.Now);
        BedtimeUnlockCountdown = FormatBedtimeCountdown(remaining);
    }

    private static string FormatBedtimeCountdown(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

        return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private void SyncPunishmentCountdownTimer()
    {
        var shouldShow = _punishment.FloorLevelIndex > 0 && _punishment.PunishmentUntil is not null;
        if (shouldShow != _showPunishmentCountdown)
        {
            _showPunishmentCountdown = shouldShow;
            OnPropertyChanged(nameof(ShowPunishmentCountdown));
            OnPropertyChanged(nameof(ShowDisciplineStatusBox));
        }

        OnPropertyChanged(nameof(PunishmentDeescalationLabel));

        if (shouldShow)
            StartPunishmentCountdownTimer();
        else
        {
            StopPunishmentCountdownTimer();
            PunishmentDeescalationCountdown = "--:--";
        }
    }

    private void StartPunishmentCountdownTimer()
    {
        _punishmentCountdownTimer ??= new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdatePunishmentCountdown(),
            _dispatcher);

        UpdatePunishmentCountdown();
        _punishmentCountdownTimer.Start();
    }

    private void StopPunishmentCountdownTimer() =>
        _punishmentCountdownTimer?.Stop();

    private void UpdatePunishmentCountdown()
    {
        if (_punishment.FloorLevelIndex <= 0 || _punishment.PunishmentUntil is not { } until)
        {
            SyncPunishmentCountdownTimer();
            return;
        }

        var remaining = until - DateTimeOffset.UtcNow;
        PunishmentDeescalationCountdown = remaining <= TimeSpan.Zero
            ? "00:00"
            : FormatPunishmentCountdown(remaining);
    }

    private static string FormatPunishmentCountdown(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

        return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private void OnScreenTimeElapsed()
    {
        var elapsed = _screenTime.Elapsed;
        ScreenTimeUsed = FormatDuration(elapsed);
        ScreenTimeLimit = FormatDuration(TimeSpan.FromMinutes(ScreenTimeLimitMinutes));
        ScreenTimeProgress = Math.Min(
            elapsed.TotalMinutes / ScreenTimeLimitMinutes * 100,
            100);

        OnPropertyChanged(nameof(ScreenTimeUsedMinutes));
        OnPropertyChanged(nameof(ScreenTimeLimitMinutes));
        OnPropertyChanged(nameof(ScreenTimeLimitText));
        OnPropertyChanged(nameof(ScreenTimeRemainingText));
        NotifyWidgetScreenTimeChanged();
        RefreshTodayRules();

        if (IsEnrolled
            && !IsScreenTimeLocked
            && !IsSoftLimitWarningVisible
            && !_screenTimeLockBypassed
            && !_softLimitWarningBypassed
            && elapsed.TotalMinutes >= ScreenTimeLimitMinutes)
        {
            ScreenTimeLimitReached();
        }
    }

    private void SyncVpnShield()
    {
        if (_host.IsEnrolled && _agentMode.Features.VpnShield)
            _vpnBlocking.Enable(_sessionState, _urlBlocking, this);
        else
            _vpnBlocking.Disable(_sessionState, _urlBlocking);
    }

    private void RefreshRestrictions()
    {
        Interlocked.Increment(ref _restrictionsRefreshSerial);
        ScheduleCollectionUpdate(ApplyRestrictionsRefresh);
    }

    private void ApplyRestrictionsRefresh()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var serial = Volatile.Read(ref _restrictionsRefreshSerial);
            var snapshot = BuildRestrictionsSnapshot();

            if (serial != Volatile.Read(ref _restrictionsRefreshSerial))
                continue;

            ApplyRestrictionSnapshot(snapshot);

            RefreshDetailItemsIfNeeded();
            OnPropertyChanged(nameof(UrlBlockingStatus));

            if (serial == Volatile.Read(ref _restrictionsRefreshSerial))
                return;
        }
    }

    private List<RestrictionItem> BuildRestrictionsSnapshot()
    {
        var items = new List<RestrictionItem>();

        items.Add(new RestrictionItem
        {
            Title = UiCopy.VpnShieldTitle,
            Description = _vpnBlocking.IsEnabled
                ? UiCopy.VpnShieldActive
                : UiCopy.VpnShieldInactive,
            IconGlyph = UiCopy.IconVpn,
            IsActive = _vpnBlocking.IsEnabled,
        });

        _playTimeRestriction = new RestrictionItem
        {
            Title = UiCopy.PlayTimeTileTitle,
            Description = GamingUsageSummary,
            IconGlyph = UiCopy.IconPlayTime,
            IsActive = true,
            NavigationTarget = "play_time",
        };
        items.Add(_playTimeRestriction);

        _youtubeTimeRestriction = new RestrictionItem
        {
            Title = UiCopy.YoutubeTileTitle,
            Description = YoutubeUsageSummary,
            IconGlyph = UiCopy.IconYoutube,
            IsActive = true,
            NavigationTarget = "youtube_time",
        };
        items.Add(_youtubeTimeRestriction);

        _studyTimeRestriction = new RestrictionItem
        {
            Title = UiCopy.StudyTimeTileTitle,
            Description = StudyTimeTileDescription,
            IconGlyph = UiCopy.IconStudy,
            IsActive = _studyTime.IsActiveNow || _studyTime.Settings.HasSchedule,
        };
        items.Add(_studyTimeRestriction);

        items.Add(new RestrictionItem
        {
            Title = UiCopy.TaskManagerBlockTitle,
            Description = _agentMode.Features.BlockTaskManager
                ? UiCopy.TaskManagerBlockActive
                : UiCopy.TaskManagerBlockInactive,
            IconGlyph = UiCopy.IconTaskManager,
            IsActive = _agentMode.Features.BlockTaskManager,
        });

        items.Add(new RestrictionItem
        {
            Title = UiCopy.ProcessKillerBlockTitle,
            Description = _agentMode.Features.BlockProcessKillers
                ? UiCopy.ProcessKillerBlockActive
                : UiCopy.ProcessKillerBlockInactive,
            IconGlyph = UiCopy.IconProcessKillers,
            IsActive = _agentMode.Features.BlockProcessKillers,
        });

        items.Add(new RestrictionItem
        {
            Title = UiCopy.SystemToolsLockTitle,
            Description = SystemToolsLockDescription,
            IconGlyph = UiCopy.IconSystemTools,
            IsActive = _agentMode.Features.HasSystemToolLock,
        });

        items.Add(new RestrictionItem
        {
            Title = UiCopy.CloseProtectionTitle,
            Description = UiCopy.CloseProtectionActive,
            IconGlyph = UiCopy.IconCloseProtection,
            IsActive = true,
        });

        items.Add(new RestrictionItem
        {
            Title = UiCopy.DomWatchingTitle,
            Description = UiCopy.DomWatchingDescription,
            IconGlyph = UiCopy.IconDomWatching,
            IsActive = true,
        });

        items.Add(new RestrictionItem
        {
            Title = UiCopy.SafeSearchTitle,
            Description = _safeSearch.IsActive
                ? UiCopy.SafeSearchActive
                : _safeSearch.LastError ?? UiCopy.SafeSearchInactive,
            IconGlyph = UiCopy.IconSafeSearch,
            IsActive = _safeSearch.IsActive,
        });

        items.Add(new RestrictionItem
        {
            Title = UiCopy.YouTubeRestrictedModeTitle,
            Description = IsYouTubeRestrictedModeEnabled && _youtubeRestricted.IsActive
                ? UiCopy.YouTubeRestrictedModeActive
                : _youtubeRestricted.LastError ?? UiCopy.YouTubeRestrictedModeInactive,
            IconGlyph = UiCopy.IconYouTubeRestrictedMode,
            IsActive = IsYouTubeRestrictedModeEnabled && _youtubeRestricted.IsActive,
        });

        var appCount = DomBlockedApps().Count;
        items.Add(new RestrictionItem
        {
            Title = UiCopy.BlockedAppsTitle,
            Description = appCount == 0
                ? UiCopy.BlockedAppsNone
                : string.Format(UiCopy.BlockedAppsTapFormat, appCount),
            IconGlyph = UiCopy.IconBlockedApps,
            IsActive = appCount > 0,
            NavigationTarget = "blocked_apps",
        });

        var siteCount = DomBlockedHosts().Count;
        items.Add(new RestrictionItem
        {
            Title = UiCopy.BlockedSitesTitle,
            Description = siteCount == 0
                ? UiCopy.BlockedSitesNone
                : string.Format(UiCopy.BlockedSitesTapFormat, siteCount),
            IconGlyph = UiCopy.IconBlockedSites,
            IsActive = siteCount > 0,
            NavigationTarget = "blocked_websites",
        });

        return items;
    }

    private void OpenRestrictionDetail(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        CurrentPage = target switch
        {
            "blocked_apps" => DashboardPage.BlockedApps,
            "blocked_websites" => DashboardPage.BlockedWebsites,
            "play_time" => DashboardPage.PlayTime,
            "youtube_time" => DashboardPage.YoutubeTime,
            _ => DashboardPage.Home,
        };

        RefreshDetailItemsIfNeeded();
    }

    private void NavigateToKioskApps()
    {
        if (!IsKioskActive)
            return;

        CurrentPage = DashboardPage.KioskApps;
    }

    private void NavigateHome()
    {
        if (IsLocalAreaPage)
            ClearLocalSettingsPinSession();

        CurrentPage = DashboardPage.Home;
        ScheduleCollectionUpdate(() =>
        {
            DetailItems.Clear();
            GameUsages.Clear();
        });
    }

    private void RefreshDetailItemsIfNeeded()
    {
        if (CurrentPage == DashboardPage.Home)
            return;

        if (CurrentPage == DashboardPage.PlayTime)
        {
            ScheduleCollectionUpdate(() =>
            {
                DetailItems.Clear();
                RefreshGameUsagesCore();
            });
            return;
        }

        if (CurrentPage == DashboardPage.YoutubeTime)
        {
            ScheduleCollectionUpdate(() => DetailItems.Clear());
            return;
        }

        var items = CurrentPage switch
        {
            DashboardPage.BlockedApps => DomBlockedApps(),
            DashboardPage.BlockedWebsites => DomBlockedHosts(),
            _ => new List<string>(),
        };

        ScheduleCollectionUpdate(() =>
        {
            DetailItems.Clear();
            foreach (var item in items)
                DetailItems.Add(item);

            OnPropertyChanged(nameof(DetailPageSubtitle));
            OnPropertyChanged(nameof(DetailEmptyMessage));
        });
    }

    private void RefreshGameUsages() => ScheduleCollectionUpdate(RefreshGameUsagesCore);

    private void RefreshGameUsagesCore()
    {
        var games = _gaming.GetBreakdown();

        GameUsages.Clear();
        foreach (var game in games)
        {
            var usageLabel = FormatDuration(game.Usage);
            GameUsages.Add(new GameUsageItem
            {
                DisplayName = game.DisplayName,
                UsageLabel = usageLabel,
                UsageSummary = usageLabel,
            });
        }

        OnPropertyChanged(nameof(DetailPageSubtitle));
        OnPropertyChanged(nameof(DetailEmptyMessage));
    }

    private void OnAppTimeLimitsChanged() =>
        PostOnUi(() =>
        {
            RefreshAppTimeLimitRules();
            OnPropertyChanged(nameof(HasAppTimeLimits));
            OnPropertyChanged(nameof(ShowAppTimeLimitsEmpty));
        });

    private void OnAppTimeLimitReached(string appName, string limitLabel)
    {
        _punishment.RegisterInfraction(
            InfractionKind.LimitIgnored,
            $"app:{appName}",
            UiCopy.InfractionGamingDetail);

        PostOnUi(() =>
        {
            if (!IsHardDisciplineEnforcement)
            {
                AddLog(string.Format(UiCopy.SoftLimitReminderFormat, appName));
                return;
            }

            AddLog($"{UiCopy.MascotName} closed {appName} — its daily time limit was reached.");
            AppBlockedPopupRequested?.Invoke(
                UiCopy.AppTimeLimitReachedTitle,
                string.Format(UiCopy.AppTimeLimitReachedMessageFormat, appName, limitLabel));
        });
    }

    public bool HasAppTimeLimits => _appTimeLimits.HasLimits;

    private void OnGamingUsageChanged()
    {
        PostOnUi(() =>
        {
            if (_playTimeRestriction is not null)
                _playTimeRestriction.Description = GamingUsageSummary;

            OnPropertyChanged(nameof(GamingUsedLabel));
            OnPropertyChanged(nameof(GamingLimitLabel));
            OnPropertyChanged(nameof(GamingRemainingLabel));
            OnPropertyChanged(nameof(GamingProgress));
            OnPropertyChanged(nameof(GamingUsageSummary));
            RefreshTodayRules();

            if (CurrentPage == DashboardPage.PlayTime)
            {
                RefreshGameUsages();
                OnPropertyChanged(nameof(DetailPageSubtitle));
                OnPropertyChanged(nameof(DetailEmptyMessage));
            }
        });
    }

    private void OnYoutubeUsageChanged()
    {
        PostOnUi(() =>
        {
            if (_youtubeTimeRestriction is not null)
                _youtubeTimeRestriction.Description = YoutubeUsageSummary;

            OnPropertyChanged(nameof(YoutubeUsedLabel));
            OnPropertyChanged(nameof(YoutubeLimitLabel));
            OnPropertyChanged(nameof(YoutubeProgress));
            OnPropertyChanged(nameof(YoutubeUsageSummary));
            RefreshTodayRules();

            if (CurrentPage == DashboardPage.YoutubeTime)
            {
                OnPropertyChanged(nameof(DetailPageSubtitle));
            }
        });
    }

    private string StudyTimeTileDescription => _studyTime.StudyTimeLabel;

    private string SystemToolsLockDescription
    {
        get
        {
            var features = _agentMode.Features;
            if (!features.HasSystemToolLock)
                return UiCopy.SystemToolsLockInactive;

            var locked = new List<string>();
            if (features.BlockRegistryEditor)
                locked.Add(UiCopy.SystemToolRegistry);
            if (features.BlockCommandPrompt)
                locked.Add(UiCopy.SystemToolCommandPrompt);
            if (features.BlockPowerShell)
                locked.Add(UiCopy.SystemToolPowerShell);
            if (features.BlockSystemConfig)
                locked.Add(UiCopy.SystemToolSystemConfig);
            if (features.BlockControlPanel)
                locked.Add(UiCopy.SystemToolControlPanel);
            if (features.BlockProcessTools)
                locked.Add(UiCopy.SystemToolProcessTools);

            return UiCopy.SystemToolsLockActiveFormat(string.Join(", ", locked));
        }
    }

    private void OnStudyDistractionAppBlocked(string appName)
    {
        var (title, message) = AppBlockMessages.Format(AppBlockCategory.StudyTime, appName);
        PostOnUi(() =>
        {
            AppBlockedPopupRequested?.Invoke(title, message);
            RegisterAppBlockInfraction(appName, appName, AppBlockCategory.StudyTime);
        });
    }

    private void OnStudyModeBlocked(string gameName)
    {
        var (title, message) = AppBlockMessages.Format(AppBlockCategory.StudyTime, gameName);

        PostOnUi(() =>
        {
            AddLog($"{UiCopy.MascotName} closed {gameName} — study time is active.");
            AppBlockedPopupRequested?.Invoke(title, message);
        });
    }

    private void OnStudyTimeSettingsChanged() =>
        PostOnUi(() =>
        {
            OnPropertyChanged(nameof(StudyTimeLabel));
            OnPropertyChanged(nameof(IsStudyModeActive));
            RefreshRestrictions();
            RefreshTodayRules();
        });

    private void OnStudyTimeActiveStateChanged() =>
        PostOnUi(() =>
        {
            var active = _studyTime.IsActiveNow;
            if (active && !_wasStudyActiveForToast)
            {
                StudyToastPopupRequested?.Invoke(
                    UiCopy.StudyStartedTitle,
                    string.Format(UiCopy.StudyStartedMessage, _studyTime.ActiveUntilLabel));
            }
            else if (!active && _wasStudyActiveForToast)
            {
                StudyToastPopupRequested?.Invoke(
                    UiCopy.StudyEndedTitle,
                    UiCopy.StudyEndedMessage);
            }

            _wasStudyActiveForToast = active;
            OnStudyTimeSettingsChanged();
        });

    public void StudyTimeSettingsReceived(StudyTimeSettingsPayload payload)
    {
        if (_localMode.IsEnabled)
            return;

        var settings = StudyTimeSettings.FromPayload(
            payload.Enabled,
            payload.StartTime,
            payload.EndTime,
            payload.Days,
            _studyTime.Settings,
            payload.Weekly,
            payload.BlockGames,
            payload.BlockYoutube,
            payload.BlockDistractingSites,
            payload.BlockDistractingApps);

        PostOnUi(() =>
        {
            _studyTime.Update(settings);
            OnPropertyChanged(nameof(StudyTimeLabel));
            OnPropertyChanged(nameof(IsStudyModeActive));
            RefreshTodayRules();
            AddLog($"{UiCopy.MascotName} updated study time to {settings.DisplayLabel}.");
        });
    }

    private void OnGameSessionStarted(DetectedGame game)
    {
        PostOnUi(() =>
            GamingSessionToastRequested?.Invoke(new GamingSessionToast(
                UiCopy.GameSessionStartTitle,
                UiCopy.GameSessionStartMessageFormat(game.DisplayName),
                UiCopy.IconPlayTime)));
    }

    private void OnGamingLimitReached(string limitLabel)
    {
        PostOnUi(() =>
        {
            if (!IsHardDisciplineEnforcement)
            {
                if (_gamingSoftLimitWarningBypassed)
                {
                    // Subsequent minutes after bypass: drain trust + nag every 3 min.
                    _punishment.RegisterInfraction(
                        InfractionKind.LimitIgnored,
                        "gaming",
                        UiCopy.InfractionGamingDetail);
                    _gamingNagTickCount++;
                    if (_gamingNagTickCount % NagEveryTicks == 0)
                        AppBlockedPopupRequested?.Invoke(
                            UiCopy.NagTitle,
                            UiCopy.NextLimitIgnoredNag("your games"));
                }
                else if (!_gamingSoftLimitOverlayOpen)
                {
                    // First time over limit: show overlay, no infraction yet.
                    ShowGamingSoftLimitOverlay(limitLabel);
                }
                return;
            }

            _punishment.RegisterInfraction(
                InfractionKind.LimitIgnored,
                "gaming",
                UiCopy.InfractionGamingDetail);
            AddLog($"{UiCopy.MascotName} closed your games — daily play time limit reached.");
            AppBlockedPopupRequested?.Invoke(
                UiCopy.PlayTimeLimitReachedTitle,
                UiCopy.PlayTimeLimitReachedMessageFormat(limitLabel));
        });
    }

    private void ShowGamingSoftLimitOverlay(string limitLabel)
    {
        _gamingSoftLimitOverlayOpen = true;
        var overlay = new Views.GamingSoftLimitOverlayWindow(limitLabel);
        overlay.Closed += (_, _) =>
        {
            _gamingSoftLimitOverlayOpen = false;
            if (!overlay.UserChoseToContinue)
            {
                // User agreed to stop — enforce the closure.
                _gaming.KillRunningGames();
                AddLog("Stopped playing after reaching the daily game limit.");
                return;
            }
            _gamingSoftLimitWarningBypassed = true;
            _punishment.RegisterInfraction(
                InfractionKind.LimitIgnored,
                "gaming",
                UiCopy.InfractionGamingDetail);
            AddLog("Kept playing past the daily game limit — Guardi's trust dipped a little.");
        };
        overlay.Show();
    }

    private void OnYoutubeLimitReached(string limitLabel)
    {
        PostOnUi(() =>
        {
            if (!IsHardDisciplineEnforcement)
            {
                // Soft mode: extension shows in-page overlay. No infraction until the user
                // explicitly clicks "Continue watching" (see OnYoutubeSoftAck). After the
                // first ack, subsequent ticks drain trust silently every minute.
                if (_youtubeSoftLimitWarningBypassed)
                {
                    _punishment.RegisterInfraction(
                        InfractionKind.LimitIgnored,
                        "youtube",
                        UiCopy.InfractionYoutubeDetail);
                    _youtubeNagTickCount++;
                    if (_youtubeNagTickCount % NagEveryTicks == 0)
                        AppBlockedPopupRequested?.Invoke(
                            UiCopy.NagTitle,
                            UiCopy.NextLimitIgnoredNag("YouTube"));
                }
                return;
            }

            _punishment.RegisterInfraction(
                InfractionKind.LimitIgnored,
                "youtube",
                UiCopy.InfractionYoutubeDetail);
            AddLog($"{UiCopy.MascotName} closed the YouTube tab — daily time limit reached.");
            AppBlockedPopupRequested?.Invoke(
                UiCopy.YoutubeLimitReachedTitle,
                UiCopy.YoutubeLimitReachedMessageFormat(limitLabel));
        });
    }

    private void OnYoutubeSoftAck()
    {
        PostOnUi(() =>
        {
            if (_youtubeSoftLimitWarningBypassed)
                return;

            _youtubeSoftLimitWarningBypassed = true;
            _punishment.RegisterInfraction(
                InfractionKind.LimitIgnored,
                "youtube",
                UiCopy.InfractionYoutubeDetail);
            AddLog("Kept watching YouTube past the daily limit — Guardi's trust dipped a little.");
        });
    }

    private void OnYoutubeStudyModeBlocked(string sourceLabel)
    {
        var (title, message) = AppBlockMessages.Format(AppBlockCategory.StudyTime, sourceLabel);

        PostOnUi(() =>
        {
            AddLog($"{UiCopy.MascotName} closed {sourceLabel} — study time is active.");
            AppBlockedPopupRequested?.Invoke(title, message);
        });
    }

    public void ModeSettingsReceived(ModeSettingsPayload payload, ScreenTimeSettingsPayload? screenTime, bool domOverride = false)
    {
        if (_localMode.IsEnabled)
            return;

        PostOnUi(() =>
        {
            if (domOverride && payload.Slug is { } slug && AgentModeSlugs.IsKnown(slug))
            {
                _punishment.ClearFloor();
                _agentMode.SetPunishmentFloor(0);
                SyncPunishmentCountdownTimer();
            }

            var before = _agentMode.Slug;
            var hadScreenTime = screenTime?.DailyLimitMinutes is not null;
            _agentMode.Apply(payload, screenTime);

            if (payload.Slug is { } appliedSlug
                && AgentModeSlugs.IsKnown(appliedSlug)
                && !string.Equals(before, _agentMode.Slug, StringComparison.Ordinal))
            {
                AddLog($"{UiCopy.MascotName} switched supervision to {_agentMode.DisplayName}.");
            }
            else if (domOverride
                     && payload.Slug is { } requestedSlug
                     && AgentModeSlugs.IsKnown(requestedSlug)
                     && string.Equals(requestedSlug, _agentMode.BaseSlug, StringComparison.Ordinal)
                     && !string.Equals(before, requestedSlug, StringComparison.Ordinal))
            {
                AddLog($"{UiCopy.MascotName} switched supervision to {_agentMode.DisplayName}.");
            }
            else if (hadScreenTime)
            {
                AddLog($"{UiCopy.MascotName} updated the daily screen allowance.");
            }
        });
    }

    private void OnAgentModeChanged() =>
        PostOnUi(() =>
        {
            RefreshModeSteps();
            OnPropertyChanged(nameof(LevelTitle));
            OnPropertyChanged(nameof(LevelSubtitle));
            OnPropertyChanged(nameof(HeaderCenterTitle));
            OnPropertyChanged(nameof(HeaderCenterSubtitle));
            OnPropertyChanged(nameof(HomeGreetingTitle));
            OnPropertyChanged(nameof(HomeGreetingSubtitle));
            OnPropertyChanged(nameof(AppSubtitle));
            OnPropertyChanged(nameof(ShowMascot));
            OnPropertyChanged(nameof(IsMatureUi));
            OnPropertyChanged(nameof(IsSubMode));
            OnPropertyChanged(nameof(IsTrustedSubMode));
            OnPropertyChanged(nameof(IsRestrictedSubMode));
            OnPropertyChanged(nameof(ShowMascotGuardi));
            OnPropertyChanged(nameof(ShowMascotTrustedSub));
            OnPropertyChanged(nameof(ShowMascotRestrictedSub));
            OnPropertyChanged(nameof(ShowLockMascotGuardi));
            OnPropertyChanged(nameof(ShowLockMascotTrustedSub));
            OnPropertyChanged(nameof(ShowLockMascotRestrictedSub));
            OnPropertyChanged(nameof(HeaderIconGlyph));
            OnPropertyChanged(nameof(MascotGreeting));
            OnPropertyChanged(nameof(StandingLabel));
            OnPropertyChanged(nameof(PersonalizedName));
            OnPropertyChanged(nameof(SecurityZoneTitle));
            OnPropertyChanged(nameof(SecurityZoneBody));
            OnPropertyChanged(nameof(ScreenTimeTitle));
            OnPropertyChanged(nameof(InfractionsTitle));
            OnPropertyChanged(nameof(InfractionsGood));
            OnPropertyChanged(nameof(InfractionsWarning));
            OnPropertyChanged(nameof(PunishmentDeescalationLabel));
            OnPropertyChanged(nameof(BedtimeTitle));
            OnPropertyChanged(nameof(DomMessageTitle));
            OnPropertyChanged(nameof(RestrictionsTitle));
            OnPropertyChanged(nameof(ActivityTitle));
            OnPropertyChanged(nameof(ActivityLogTitle));
            OnPropertyChanged(nameof(PlayTimeDetailTotalLabel));
            OnPropertyChanged(nameof(GameListIconGlyph));
            OnPropertyChanged(nameof(EnrollmentTitle));
            OnPropertyChanged(nameof(EnrollmentBody));
            OnPropertyChanged(nameof(EnrollmentLevelLabel));
            OnPropertyChanged(nameof(EnrollmentCodeLabel));
            OnPropertyChanged(nameof(EnrollmentNameLabel));
            OnPropertyChanged(nameof(EnrollmentButton));
            OnPropertyChanged(nameof(LockOverlayStatsLabel));
            OnPropertyChanged(nameof(LockOverlayHeadline));
            OnPropertyChanged(nameof(ShowLockMascot));
            OnPropertyChanged(nameof(ScreenTimeLimitMinutes));
            OnPropertyChanged(nameof(ScreenTimeLimitText));
            OnPropertyChanged(nameof(RunningAppsLabel));
            OnPropertyChanged(nameof(GamingUsageSummary));
            OnPropertyChanged(nameof(DetailPageTitle));
            OnPropertyChanged(nameof(DetailPageSubtitle));
            OnPropertyChanged(nameof(DetailEmptyMessage));
            OnPropertyChanged(nameof(OnlineLabel));
            SyncVpnShield();
            ReconcileImageShield();
            RefreshRestrictions();
            SyncPunishmentCountdownTimer();

            // A changed daily limit (Dom edit, mode switch, etc.) re-arms the soft warning —
            // otherwise reaching a newly-lowered/raised limit again would stay silently bypassed.
            if (_lastScreenTimeLimitMinutesForBypass is { } previous && previous != ScreenTimeLimitMinutes)
                _softLimitWarningBypassed = false;
            _lastScreenTimeLimitMinutesForBypass = ScreenTimeLimitMinutes;

            OnScreenTimeElapsed();
            SyncKioskState();
            ModePresentationChanged?.Invoke();
            RefreshTodayRules();
        });

    public event Action? ModePresentationChanged;

    private void OnSecurityToolBlocked(string exe)
    {
        if (!IsEnrolled)
            return;

        var displayName = AppDisplayNames.Resolve(exe);
        _punishment.RegisterInfraction(
            InfractionKind.BypassAttempt,
            $"tool:{exe}",
            string.Format(UiCopy.InfractionBypassToolDetail, displayName));

        PostOnUi(() =>
        {
            AddLog($"{UiCopy.MascotName} blocked {displayName}.");
            AppBlockedPopupRequested?.Invoke(
                UiCopy.SecurityToolBlockedTitle,
                string.Format(UiCopy.SecurityToolBlockedMessage, displayName));
        });
    }

    private void OnExtensionBlockedSearch(string query, string match)
    {
        if (!IsEnrolled && !IsLocalMode)
            return;

        var label = string.IsNullOrWhiteSpace(match) ? "grown-up content" : match;
        _punishment.RegisterInfraction(
            InfractionKind.BlockedSearch,
            $"search:{label}",
            string.Format(UiCopy.InfractionBlockedSearchDetailFormat, label));

        PostOnUi(() => AddLog($"{UiCopy.MascotName} blocked a naughty search, little one."));
    }

    private void OnInfractionRegistered(InfractionRecord record) =>
        ScheduleCollectionUpdate(() =>
        {
            Infractions.Insert(0, new InfractionItem
            {
                Label = InfractionKindLabel(record.Kind),
                Detail = record.Detail,
                Time = record.At.ToLocalTime().ToString("HH:mm"),
                TrustPointsLost = record.TrustPointsLost,
            });

            while (Infractions.Count > 25)
                Infractions.RemoveAt(Infractions.Count - 1);

            AddLogCore($"{UiCopy.InfractionLogPrefix}: {record.Detail}");
            RefreshDisciplineStanding();
        });

    private void OnPunishmentEscalated(int fromIndex, int toIndex) => PostOnUi(() =>
    {
        var modeName = AgentModeRegistry.AtStrictnessIndex(toIndex).DisplayName;
        AppBlockedPopupRequested?.Invoke(
            UiCopy.PunishmentEscalationTitle,
            string.Format(UiCopy.PunishmentEscalationMessage, modeName));
    });

    private void OnPunishmentFloorChanged(int floorIndex) => PostOnUi(() =>
    {
        _agentMode.SetPunishmentFloor(floorIndex);
        AddLog(string.Format(UiCopy.PunishmentLevelChangedLog, _agentMode.DisplayName));
        SyncPunishmentCountdownTimer();
        SyncKioskState();
        RefreshDisciplineStanding();
        RefreshTodayRules();
    });

    private void OnPunishmentStateChanged() => PostOnUi(() =>
    {
        RefreshDisciplineStanding();
        SyncPunishmentCountdownTimer();
    });

    private void RefreshDisciplineStanding()
    {
        OnPropertyChanged(nameof(InfractionCount));
        OnPropertyChanged(nameof(IsDisciplineEnabled));
        OnPropertyChanged(nameof(ShowDisciplineProgress));
        OnPropertyChanged(nameof(DisciplineProgressText));
        OnPropertyChanged(nameof(DisciplineProgressValue));
        OnPropertyChanged(nameof(TrustValue));
        OnPropertyChanged(nameof(IsDisciplineEscalated));
        OnPropertyChanged(nameof(DisciplineModeStatus));
        OnPropertyChanged(nameof(ShowDisciplineStatusBox));
        OnPropertyChanged(nameof(ShowInfractionsGood));
        OnPropertyChanged(nameof(HasRecentInfractions));
        RefreshTodayRules();
    }

    private string BuildDisciplineProgressText()
    {
        if (!IsDisciplineEnabled)
            return UiCopy.DisciplineDisabledHint;

        // The gauge itself communicates "how close to stricter mode"; show its zone message.
        return UiCopy.TrustZoneText(_punishment.TrustValue, _punishment.Zone);
    }

    private string BuildDisciplineModeStatus()
    {
        if (!IsDisciplineEnabled)
            return UiCopy.DisciplineDisabledHint;

        if (IsDisciplineEscalated)
        {
            return string.Format(
                UiCopy.DisciplineEscalatedFormat,
                _agentMode.BaseDisplayName,
                _agentMode.DisplayName);
        }

        return BuildDisciplineProgressText();
    }

    private static string InfractionKindLabel(InfractionKind kind) => kind switch
    {
        InfractionKind.VpnAttempt => UiCopy.InfractionLabelVpn,
        InfractionKind.BlockedAppRepeated => UiCopy.InfractionLabelBlockedApp,
        InfractionKind.BypassAttempt => UiCopy.InfractionLabelBypass,
        InfractionKind.LimitIgnored => UiCopy.InfractionLabelLimit,
        InfractionKind.StudyTimeViolation => UiCopy.InfractionLabelStudy,
        InfractionKind.BlockedSearch => UiCopy.InfractionLabelBlockedSearch,
        _ => UiCopy.InfractionLabelGeneric,
    };

    public void PunishmentSettingsReceived(PunishmentSettingsPayload payload) => PostOnUi(() =>
    {
        if (_localMode.IsEnabled)
            return;

        _punishment.ApplySettings(payload);
        RefreshDisciplineStanding();
        AddLog(UiCopy.PunishmentSettingsUpdatedLog);
    });

    public void PunishmentResetRequested() => PostOnUi(() =>
    {
        if (_localMode.IsEnabled)
            return;

        _punishment.Reset();
        _agentMode.SetPunishmentFloor(0);
        SyncPunishmentCountdownTimer();
        ScheduleCollectionUpdate(() =>
        {
            Infractions.Clear();
            RefreshDisciplineStanding();
            AddLogCore(UiCopy.PunishmentResetLog);
        });
    });

    private string? _imageShieldSignature;

    private void ReconcileImageShield() =>
        _ = Task.Run(() => ReconcileImageShieldCore(CancellationToken.None));

    private void ReconcileImageShieldCore(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var modeSlug = _agentMode.Slug;
        var signature = _imageShieldPolicy.ComputeSignature(modeSlug);
        var shouldRun = _imageShieldPolicy.IsEffectivelyEnabled(modeSlug);
        var runtimeOn = ImageShieldRuntimeStore.IsFilteringActive;
        var policiesOn = _imageShieldPolicy.PoliciesActive;

        // Settings unchanged, but a prior quit left runtime/policies off while Dom still wants the shield on.
        var needsRuntimeResync = shouldRun && (!runtimeOn || !policiesOn);
        var needsRuntimeShutdown = !shouldRun && (runtimeOn || policiesOn);

        if (signature == _imageShieldSignature && !needsRuntimeResync && !needsRuntimeShutdown)
            return;

        _imageShieldSignature = signature;

        if (!shouldRun)
        {
            AuditLog.Write("Image shield skipped — disabled by Dom policy or current supervision mode.");
            _extensionGuard.Stop();
            ImageShieldRuntimeStore.SetFilteringActive(false);
            if (_imageShield.HasPersistedInstall)
            {
                var offSettings = _imageShieldPolicy.BuildTuningSettings(modeSlug) with { ShieldActive = false };
                _imageShield.SetRuntimeActive(false, offSettings);
            }
            _imageShieldPolicy.SetPoliciesActive(false);
            PostOnUi(() =>
            {
                RefreshProtectionStatus();
                AddLog($"{UiCopy.MascotName} turned off the image shield.");
            });
            return;
        }

        if (!_imageShield.IsConfigured)
        {
            AuditLog.Write(
                $"Image shield not applied: {ExtensionStoreConfigLoader.LastError ?? _imageShield.LastError ?? "not configured"}");
            _extensionGuard.Stop();
            _imageShieldPolicy.SetPoliciesActive(false);
            PostOnUi(RefreshProtectionStatus);
            return;
        }

        var settings = _imageShieldPolicy.BuildTuningSettings(_agentMode.Slug);
        bool applied;
        if (_imageShield.HasPersistedInstall)
        {
            _imageShield.SetRuntimeActive(true, settings);
            _imageShield.ApplyFirefoxSignedEnterprisePolicies(settings, requireDomToggle: false);
            applied = true;
        }
        else
        {
            applied = _imageShield.Apply(settings);
        }
        _imageShieldPolicy.SetPoliciesActive(applied && _imageShield.IsActive);

        if (Config.ExtensionGuardEnabled && ExtensionConfigResolver.IsReady)
            _extensionGuard.Start();
        else
            _extensionGuard.Stop();

        PostOnUi(() =>
        {
            RefreshProtectionStatus();
            if (applied)
            {
                AddLog($"{UiCopy.MascotName} enabled the image shield (restart browsers to load it).");
                if (Config.ExtensionGuardEnforceChromium)
                {
                    _imageShield.TryRestartChromeForShield(
                        settings,
                        log: AddLog,
                        onRestarting: (headline, body) =>
                            FirefoxRestartToastRequested?.Invoke(headline, body));
                }

                if (Config.ExtensionGuardEnforceFirefox && !Config.ExtensionGuardFirefoxLocalMode)
                {
                    _imageShield.TryRestartFirefoxForShield(
                        settings,
                        log: AddLog,
                        onRestarting: (headline, body) =>
                            FirefoxRestartToastRequested?.Invoke(headline, body),
                        enforceBrowserPolicy: false);
                }
            }
            else
                AddLog(_imageShield.LastError ?? "Image shield could not be applied.");
        });
    }

    public void ImageShieldSettingsReceived(ImageShieldSettingsPayload payload)
    {
        if (_localMode.IsEnabled)
            return;

        var policyChanged = _imageShieldPolicy.ApplyFromServer(payload);
        if (!policyChanged && _imageShieldSignature is not null)
            return;

        _imageShieldSignature = null;
        ReconcileImageShield();
    }

    private void RefreshModeSteps()
    {
        _urlBlocking.SetMode(_agentMode.DisplayName, _agentMode.Theme);

        var steps = _agentMode.BuildModeSteps().ToList();

        void Apply()
        {
            LevelSteps.Clear();
            foreach (var step in steps)
                LevelSteps.Add(step);
        }

        ScheduleCollectionUpdate(Apply);
    }

    public void GamingSettingsReceived(GamingSettingsPayload payload, bool replaceGameLists = false)
    {
        if (_localMode.IsEnabled)
            return;

        PostOnUi(() =>
        {
            var limitBefore = _gaming.LimitMinutes;
            _gaming.ApplySettings(payload, replaceGameLists);

            if (payload.DailyLimitMinutes is { } limit && limit != limitBefore)
            {
                AddLog($"{UiCopy.MascotName} updated the daily play time limit to {FormatDuration(_gaming.LimitDuration)}.");
                _gamingSoftLimitWarningBypassed = false;
            }

            OnGamingUsageChanged();
        });
    }

    public void YoutubeSettingsReceived(YoutubeSettingsPayload payload)
    {
        if (_localMode.IsEnabled)
            return;

        PostOnUi(() =>
        {
            var limitBefore = _youtube.LimitMinutes;
            var restrictedBefore = _youtube.RestrictedModeEnabled;
            _youtube.ApplySettings(payload);

            if (payload.DailyLimitMinutes is { } limit && limit != limitBefore)
            {
                AddLog($"{UiCopy.MascotName} updated the daily YouTube limit to {FormatDuration(_youtube.LimitDuration)}.");

                // Re-arm the soft warning — a changed limit reached again should drop trust again.
                _youtubeSoftLimitWarningBypassed = false;
            }

            if (payload.RestrictedModeEnabled is { } restricted && restricted != restrictedBefore)
                ReconcileYoutubeRestrictedMode();
        });
    }

    private void ReconcileYoutubeRestrictedMode() =>
        Task.Run(() => ReconcileYoutubeRestrictedModeCore(CancellationToken.None));

    private void ReconcileYoutubeRestrictedModeCore(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!IsYouTubeRestrictedModeEnabled)
        {
            var wasActive = _youtubeRestricted.IsActive || _youtubeRestricted.HasOrphanedState();
            _youtubeRestricted.Release();
            PostOnUi(() =>
            {
                RefreshProtectionStatus();
                if (wasActive)
                    AddLog($"{UiCopy.MascotName} turned off YouTube restricted mode.");
            });
            return;
        }

        var applied = _youtubeRestricted.Apply();
        PostOnUi(() =>
        {
            RefreshProtectionStatus();
            if (applied)
                AddLog($"{UiCopy.MascotName} locked YouTube restricted mode in your browsers (restart them if already open).");
            else
                AddLog(_youtubeRestricted.LastError ?? UiCopy.YouTubeRestrictedModeInactive);
        });
    }

    public void KioskSettingsReceived(KioskSettingsPayload payload) =>
        PostOnUi(() =>
        {
            if (_localMode.IsEnabled)
                return;

            var countBefore = _kioskApps.Apps.Count;
            _kioskApps.ApplySettings(payload);
            if (_kioskApps.Apps.Count != countBefore)
                AddLog($"{UiCopy.MascotName} updated the kiosk app list ({_kioskApps.Apps.Count} approved).");
        });

    public void RegisterKioskWindow(IntPtr handle) => _kiosk.SetKioskWindow(handle);

    private void RefreshKioskApps()
    {
        var snapshot = _kioskApps.Apps;

        void Apply()
        {
            var items = snapshot
                .Select(app => new KioskAppItem
                {
                    Name = app.Name,
                    Path = app.Path,
                    Args = app.Args,
                    IconImage = ExecutableIconService.GetForPath(app.Path),
                    IconGlyph = string.IsNullOrWhiteSpace(app.Icon) ? "\U0001F4E6" : app.Icon!,
                })
                .ToList();

            KioskApps.Clear();
            foreach (var item in items)
                KioskApps.Add(item);

            OnPropertyChanged(nameof(HasKioskApps));
        }

        if (_dispatcher.CheckAccess())
            _dispatcher.BeginInvoke(DispatcherPriority.Background, Apply);
        else
            _dispatcher.BeginInvoke(Apply);
    }

    private void LaunchKioskApp(KioskAppItem? app)
    {
        if (app is null)
            return;

        try
        {
            var launched = _kiosk.LaunchApp(new KioskApp(app.Name, app.Path, app.Args, null));
            if (!launched)
                AddLog($"Could not open {app.Name}. Ask your Dom to check the path.");
        }
        catch (Exception ex)
        {
            AddLog($"Failed to launch {app.Name}: {ex.Message}");
        }
    }

    private void RequestKioskExit()
    {
        if (!TryAuthorizeProtectedAction("kiosk_exit"))
            return;

        _kioskBypassed = true;
        SyncKioskState();
        AddLog($"{UiCopy.MascotName} unlocked the kiosk with the Dom PIN.");
    }

    private void SyncKioskState()
    {
        try
        {
            var shouldBeActive = (IsEnrolled || IsLocalMode)
                && _agentMode.Features.KioskMode
                && !_kioskBypassed;

            if (shouldBeActive)
            {
                var wasActive = _kiosk.IsActive;
                if (!wasActive)
                    _kiosk.Activate();
                if (!wasActive)
                    NavigateHome();
            }
            else
            {
                _kiosk.ForceRelease();
                if (CurrentPage == DashboardPage.KioskApps)
                    NavigateHome();
            }

            if (shouldBeActive == _kioskPresentationActive)
                return;

            NotifyKioskPresentationChanged(shouldBeActive);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"SyncKioskState failed: {ex}");
            EmergencyReleaseKiosk();
        }
    }

    private void NotifyKioskPresentationChanged(bool active)
    {
        _kioskPresentationActive = active;
        OnPropertyChanged(nameof(IsKioskActive));
        NavigateToKioskAppsCommand.RaiseCanExecuteChanged();
        try
        {
            KioskStateChanged?.Invoke(active);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"KioskStateChanged handler failed: {ex}");
            EmergencyReleaseKiosk();
        }
    }

    private List<string> DomBlockedApps() =>
        _sessionState.GetBlockedAppsExcept(AppBlockCategory.VpnShield).ToList();

    private List<string> DomBlockedHosts() =>
        _urlBlocking.BlockedHosts
            .Where(host => !VpnBlocklist.IsManagedHost(host))
            .OrderBy(host => host, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ApplyRestrictionSnapshot(IReadOnlyList<RestrictionItem> snapshot)
    {
        if (Restrictions.Count == snapshot.Count)
        {
            var sameShape = true;
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (!SameRestrictionShape(Restrictions[i], snapshot[i]))
                {
                    sameShape = false;
                    break;
                }
            }

            if (sameShape)
            {
                for (var i = 0; i < snapshot.Count; i++)
                    Restrictions[i].Description = snapshot[i].Description;
                return;
            }
        }

        Restrictions.Clear();
        foreach (var item in snapshot)
            Restrictions.Add(item);
    }

    private static bool SameRestrictionShape(RestrictionItem current, RestrictionItem next) =>
        current.Title == next.Title
        && current.IconGlyph == next.IconGlyph
        && current.IsActive == next.IsActive
        && current.NavigationTarget == next.NavigationTarget
        && current.IsPlaceholder == next.IsPlaceholder;

    private void ScheduleCollectionUpdate(Action action)
    {
        if (_disposed)
            return;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }

    private async Task EnrollAsync()
    {
        IsConnecting = true;
        EnrollmentDetail = UiCopy.EnrollmentConnecting;

        try
        {
            var ok = await _host.RegisterAsync(
                this,
                EnrollmentCode.Trim(),
                string.IsNullOrWhiteSpace(PcName) ? Environment.MachineName : PcName.Trim(),
                CancellationToken.None);

            if (ok)
                SyncVpnShield();
            else
                EnrollmentDetail = UiCopy.EnrollmentFailed;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void ResetEnrollment()
    {
        if (!TryAuthorizeProtectedAction("unlink"))
            return;

        _screenTimeLockBypassed = false;
        _softLimitWarningBypassed = false;
        _youtubeSoftLimitWarningBypassed = false;
        _gamingSoftLimitWarningBypassed = false;
        _host.ResetEnrollment(this);
        EnrollmentCode = string.Empty;
        _punishment.Reset();
        _agentMode.SetPunishmentFloor(0);
        SyncPunishmentCountdownTimer();
        ScheduleCollectionUpdate(() => Infractions.Clear());
        NavigateHome();
        RefreshRestrictions();
    }

    private void AddLog(string message) =>
        ScheduleCollectionUpdate(() => AddLogCore(message));

    private void AddLogCore(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ActivityLog.Insert(0, line);
        while (ActivityLog.Count > 50)
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "0m";

        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        return $"{(int)Math.Ceiling(duration.TotalMinutes)}m";
    }

    private void PostOnUi(Action action)
    {
        if (_disposed)
            return;

        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(action);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Func<Task>? _asyncExecute;
    private readonly Action? _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Func<Task> asyncExecute, Func<bool>? canExecute = null)
    {
        _asyncExecute = asyncExecute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        if (_asyncExecute is not null)
            await _asyncExecute();
        else
            _execute?.Invoke();
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class RelayCommand<T> : System.Windows.Input.ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        _canExecute?.Invoke(parameter is T typed ? typed : default) ?? true;

    public void Execute(object? parameter) =>
        _execute(parameter is T typed ? typed : default);

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}



