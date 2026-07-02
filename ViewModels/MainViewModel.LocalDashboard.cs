using System.Collections.ObjectModel;
using System.Windows;
using EduGuardAgent.Agent;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.ViewModels;

internal sealed partial class MainViewModel
{
    public bool IsLocalHubPage => CurrentPage == DashboardPage.LocalHub;
    public bool IsLocalSectionPage => CurrentPage == DashboardPage.LocalSection;
    public bool IsLocalAreaPage => IsLocalHubPage || IsLocalSectionPage;

    public string LocalHubTitle => LocalModeCopy.HubTitle;
    public string LocalHubSubtitle => LocalModeCopy.HubSubtitle;

    public ObservableCollection<LocalDayLimitItem> LocalScreenWeekly { get; } = [];
    public ObservableCollection<LocalDayLimitItem> LocalGamingWeekly { get; } = [];
    public ObservableCollection<LocalDayLimitItem> LocalYoutubeWeekly { get; } = [];
    public ObservableCollection<LocalDayBedtimeItem> LocalBedtimeWeekly { get; } = [];
    public ObservableCollection<LocalDayStudyItem> LocalStudyWeekly { get; } = [];
    public ObservableCollection<LocalModeChoiceItem> LocalModeChoices { get; } = [];
    public ObservableCollection<LocalKioskAppItem> LocalKioskApps { get; } = [];
    public ObservableCollection<LocalAppTimeLimitItem> LocalAppTimeLimits { get; } = [];
    public ObservableCollection<LocalCustomGameItem> LocalCustomGames { get; } = [];
    public ObservableCollection<LocalWebCategoryItem> LocalWebCategories { get; } = [];

    private static readonly (string Key, string Label)[] WeekDays =
    [
        ("mon", "Mon"),
        ("tue", "Tue"),
        ("wed", "Wed"),
        ("thu", "Thu"),
        ("fri", "Fri"),
        ("sat", "Sat"),
        ("sun", "Sun"),
    ];

    public string LocalHeaderTitle => IsLocalHubPage
        ? LocalHubTitle
        : LocalSectionTitle;

    public string LocalHeaderSubtitle => IsLocalHubPage
        ? LocalHubSubtitle
        : LocalSectionSubtitle;

    public string LocalSectionTitle => LocalSettingsService.HubCards
        .FirstOrDefault(c => string.Equals(c.Key, _localSectionKey, StringComparison.OrdinalIgnoreCase))?.Title
        ?? string.Empty;

    public string LocalSectionSubtitle => LocalSettingsService.HubCards
        .FirstOrDefault(c => string.Equals(c.Key, _localSectionKey, StringComparison.OrdinalIgnoreCase))?.Subtitle
        ?? string.Empty;

    public bool LocalSectionUsesModePicker =>
        _localSectionKey is "supervision" or "bedtime" or "playtime" or "youtube" or "study" or "app_limits";

    public bool IsLocalSupervisionSection => _localSectionKey == "supervision";
    public bool IsLocalKioskSection => _localSectionKey == "kiosk";
    public bool IsLocalBedtimeSection => _localSectionKey == "bedtime";
    public bool IsLocalPlaytimeSection => _localSectionKey == "playtime";
    public bool IsLocalAppLimitsSection => _localSectionKey == "app_limits";
    public bool IsLocalYoutubeSection => _localSectionKey == "youtube";
    public bool IsLocalStudySection => _localSectionKey == "study";
    public bool IsLocalDisciplineSection => _localSectionKey == "discipline";
    public bool IsLocalImageShieldSection => _localSectionKey == "image_shield";
    public bool IsLocalSecuritySection => _localSectionKey == "security";
    public bool IsLocalBlocklistSection => _localSectionKey == "blocklist";
    public bool IsLocalControlSection => _localSectionKey == "control";
    public bool IsLocalScreenshotsSection => _localSectionKey == "screenshots";
    public bool IsLocalAppearanceSection => _localSectionKey == "appearance";

    public bool HasLocalSyncWarning => !string.IsNullOrWhiteSpace(_localSyncWarning);

    public string LocalSyncWarning
    {
        get => _localSyncWarning;
        private set
        {
            if (SetField(ref _localSyncWarning, value))
                OnPropertyChanged(nameof(HasLocalSyncWarning));
        }
    }

    public IReadOnlyList<string> LocalModeSlugOptions { get; } =
    [
        AgentModeSlugs.TrustedSub,
        AgentModeSlugs.Sub,
        AgentModeSlugs.RestrictedSub,
    ];

    public RelayCommand<string> SelectLocalEditingModeCommand { get; private set; } = null!;
    public RelayCommand<string> SelectLocalActiveModeCommand { get; private set; } = null!;
    public RelayCommand SaveLocalActiveModeCommand { get; private set; } = null!;
    public RelayCommand ClearLocalPunishmentCommand { get; private set; } = null!;
    public bool HasLocalKioskApps => LocalKioskApps.Count > 0;

    public RelayCommand RefreshLocalKioskAppsCommand { get; private set; } = null!;
    public RelayCommand AddLocalKioskAppCommand { get; private set; } = null!;
    public RelayCommand BrowseLocalKioskAppCommand { get; private set; } = null!;
    public RelayCommand CopyImageShieldDiagnosticCommand { get; private set; } = null!;
    public RelayCommand AddLocalAppTimeLimitCommand { get; private set; } = null!;
    public RelayCommand<LocalAppTimeLimitItem> RemoveLocalAppTimeLimitCommand { get; private set; } = null!;
    public RelayCommand BrowseLocalCustomGameCommand { get; private set; } = null!;
    public RelayCommand AddLocalCustomGameCommand { get; private set; } = null!;
    public RelayCommand<LocalCustomGameItem> RemoveLocalCustomGameCommand { get; private set; } = null!;
    public RelayCommand RemoveLocalExitPinCommand { get; private set; } = null!;

    public bool LocalActiveModeIsDirty =>
        !string.Equals(_localSettings.ActiveModeSlug, LocalActiveModeSlug, StringComparison.OrdinalIgnoreCase);

    public void InitializeLocalDashboardCommands()
    {
        SelectLocalEditingModeCommand = new RelayCommand<string>(
            slug =>
            {
                if (AgentModeSlugs.IsKnown(slug))
                    LocalEditingModeSlug = slug!;
            },
            slug => IsLocalMode && AgentModeSlugs.IsKnown(slug));

        SelectLocalActiveModeCommand = new RelayCommand<string>(
            slug =>
            {
                if (AgentModeSlugs.IsKnown(slug))
                    LocalActiveModeSlug = slug!;
            },
            slug => IsLocalMode && AgentModeSlugs.IsKnown(slug));

        SaveLocalActiveModeCommand = new RelayCommand(
            SaveLocalActiveMode,
            () => IsLocalMode && LocalActiveModeIsDirty);

        ClearLocalPunishmentCommand = new RelayCommand(
            ClearLocalPunishment,
            () => IsLocalMode);

        RefreshLocalKioskAppsCommand = new RelayCommand(
            RefreshLocalKioskApps,
            () => IsLocalMode);

        AddLocalKioskAppCommand = new RelayCommand(
            AddLocalKioskApp,
            () => IsLocalMode && !string.IsNullOrWhiteSpace(LocalKioskManualPath));

        BrowseLocalKioskAppCommand = new RelayCommand(
            BrowseLocalKioskApp,
            () => IsLocalMode);

        CopyImageShieldDiagnosticCommand = new RelayCommand(
            CopyImageShieldDiagnostic,
            () => IsLocalMode);

        AddLocalAppTimeLimitCommand = new RelayCommand(
            AddLocalAppTimeLimit,
            () => IsLocalMode && !string.IsNullOrWhiteSpace(LocalAppLimitExe) && LocalAppLimitMinutes > 0);

        RemoveLocalAppTimeLimitCommand = new RelayCommand<LocalAppTimeLimitItem>(
            RemoveLocalAppTimeLimit,
            _ => IsLocalMode);

        BrowseLocalCustomGameCommand = new RelayCommand(
            BrowseLocalCustomGame,
            () => IsLocalMode);

        AddLocalCustomGameCommand = new RelayCommand(
            AddLocalCustomGame,
            () => IsLocalMode
                && GamingGameRegistry.IsValidExe(LocalNewGameExe)
                && !string.IsNullOrWhiteSpace(LocalNewGameName));

        RemoveLocalCustomGameCommand = new RelayCommand<LocalCustomGameItem>(
            RemoveLocalCustomGame,
            _ => IsLocalMode);

        RemoveLocalExitPinCommand = new RelayCommand(
            RemoveLocalExitPin,
            () => IsLocalMode && LocalExitPinIsSet);

        foreach (var mode in AgentModeRegistry.All)
        {
            LocalModeChoices.Add(new LocalModeChoiceItem
            {
                Slug = mode.Slug,
                Label = mode.DisplayName,
            });
        }
    }

    public string LocalEditingModeSlug
    {
        get => _localEditingModeSlug;
        set
        {
            if (SetField(ref _localEditingModeSlug, AgentModeSlugs.Normalize(value)))
                LoadSectionFormFromCatalog();
        }
    }

    public string LocalActiveModeSlug
    {
        get => _localActiveModeSlug;
        set
        {
            if (SetField(ref _localActiveModeSlug, AgentModeSlugs.Normalize(value)))
            {
                OnPropertyChanged(nameof(LocalActiveModeIsDirty));
                SaveLocalActiveModeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void ClearLocalPunishment()
    {
        if (!IsLocalMode)
            return;

        if (!TryAuthorizeRestrictionChange("clear punishment"))
            return;

        _punishment.Reset();
        _agentMode.SetPunishmentFloor(0);
        SyncPunishmentCountdownTimer();
        RefreshModeSteps();
        OnPropertyChanged(nameof(LevelTitle));
        OnPropertyChanged(nameof(LevelSubtitle));
        OnPropertyChanged(nameof(HeaderCenterTitle));
        OnPropertyChanged(nameof(HeaderCenterSubtitle));
        OnPropertyChanged(nameof(HomeGreetingTitle));
        OnPropertyChanged(nameof(HomeGreetingSubtitle));
        OnPropertyChanged(nameof(InfractionCount));
        SyncKioskState();
        RefreshDisciplineStanding();
        ScheduleCollectionUpdate(() => Infractions.Clear());
        AddLog(LocalModeCopy.ClearPunishmentLog);
    }

    private void SaveLocalActiveMode()
    {
        if (!IsLocalMode || !LocalActiveModeIsDirty)
            return;

        if (!TryAuthorizeRestrictionChange("active supervision mode"))
            return;

        try
        {
            _kioskBypassed = false;
            _punishment.ClearFloor();
            _agentMode.SetPunishmentFloor(0);

            _localSettings.ActiveModeSlug = LocalActiveModeSlug;
            _localSettings.ApplyActiveMode();
            RefreshModeSteps();
            OnPropertyChanged(nameof(BedtimeLabel));
            OnPropertyChanged(nameof(BedtimeCardLabel));
            OnPropertyChanged(nameof(LevelTitle));
            OnPropertyChanged(nameof(LevelSubtitle));
            OnPropertyChanged(nameof(HeaderCenterTitle));
            OnPropertyChanged(nameof(HeaderCenterSubtitle));
            OnPropertyChanged(nameof(HomeGreetingTitle));
            OnPropertyChanged(nameof(HomeGreetingSubtitle));
            OnScreenTimeElapsed();
            RefreshRestrictions();
            AddLog($"Active supervision mode set to {AgentModeRegistry.Get(LocalActiveModeSlug).DisplayName}.");
        }
        catch (Exception ex)
        {
            AuditLog.Write($"SaveLocalActiveMode failed: {ex}");
            AddLog($"Could not apply supervision mode: {ex.Message}");
        }
        finally
        {
            SyncKioskState();
            OnPropertyChanged(nameof(LocalActiveModeIsDirty));
            SaveLocalActiveModeCommand.RaiseCanExecuteChanged();
        }
    }

    public int LocalScreenTimeMinutes
    {
        get => _localScreenTimeMinutes;
        set => SetField(ref _localScreenTimeMinutes, value);
    }

    private int _localScreenTimeMinutes = 480;

    public bool LocalBedtimeEnabled
    {
        get => _localBedtimeEnabled;
        set => SetField(ref _localBedtimeEnabled, value);
    }

    private bool _localBedtimeEnabled = true;

    public string LocalBedtimeTime
    {
        get => _localBedtimeTime;
        set => SetField(ref _localBedtimeTime, value);
    }

    private string _localBedtimeTime = "23:00";

    public string LocalWakeTime
    {
        get => _localWakeTime;
        set => SetField(ref _localWakeTime, value);
    }

    private string _localWakeTime = "07:00";

    public bool LocalBlueLightFilterEnabled
    {
        get => _localBlueLightFilterEnabled;
        set => SetField(ref _localBlueLightFilterEnabled, value);
    }

    private bool _localBlueLightFilterEnabled = true;

    // --- Appearance section (global visual toggles) ------------------------
    public bool LocalShowDesktopWidget
    {
        get => _localShowDesktopWidget;
        set => SetField(ref _localShowDesktopWidget, value);
    }

    private bool _localShowDesktopWidget = true;

    public bool LocalShowGamingTimer
    {
        get => _localShowGamingTimer;
        set => SetField(ref _localShowGamingTimer, value);
    }

    private bool _localShowGamingTimer = true;

    public bool LocalShowYoutubeTimer
    {
        get => _localShowYoutubeTimer;
        set => SetField(ref _localShowYoutubeTimer, value);
    }

    private bool _localShowYoutubeTimer = true;

    public bool LocalStyledNewTabPage
    {
        get => _localStyledNewTabPage;
        set => SetField(ref _localStyledNewTabPage, value);
    }

    private bool _localStyledNewTabPage = true;

    public bool LocalShowWebsiteBadge
    {
        get => _localShowWebsiteBadge;
        set => SetField(ref _localShowWebsiteBadge, value);
    }

    private bool _localShowWebsiteBadge = true;

    /// <summary>Live flag the desktop widget window binds its visibility to.</summary>
    public bool DesktopWidgetVisible =>
        (IsEnrolled || IsLocalMode) && _localSettings.Catalog.ShowDesktopWidget;

    public int LocalGamingLimitMinutes
    {
        get => _localGamingLimitMinutes;
        set => SetField(ref _localGamingLimitMinutes, value);
    }

    private int _localGamingLimitMinutes = 60;

    public string LocalNewGameExe
    {
        get => _localNewGameExe;
        set
        {
            if (SetField(ref _localNewGameExe, value))
                AddLocalCustomGameCommand.RaiseCanExecuteChanged();
        }
    }

    private string _localNewGameExe = string.Empty;

    public string LocalNewGameName
    {
        get => _localNewGameName;
        set
        {
            if (SetField(ref _localNewGameName, value))
                AddLocalCustomGameCommand.RaiseCanExecuteChanged();
        }
    }

    private string _localNewGameName = string.Empty;

    public string LocalAppLimitExe
    {
        get => _localAppLimitExe;
        set
        {
            if (SetField(ref _localAppLimitExe, value))
                AddLocalAppTimeLimitCommand.RaiseCanExecuteChanged();
        }
    }

    private string _localAppLimitExe = string.Empty;

    public int LocalAppLimitMinutes
    {
        get => _localAppLimitMinutes;
        set
        {
            if (SetField(ref _localAppLimitMinutes, value))
                AddLocalAppTimeLimitCommand.RaiseCanExecuteChanged();
        }
    }

    private int _localAppLimitMinutes = 30;

    public int LocalYoutubeLimitMinutes
    {
        get => _localYoutubeLimitMinutes;
        set => SetField(ref _localYoutubeLimitMinutes, value);
    }

    private int _localYoutubeLimitMinutes = 30;

    public bool LocalGamingShowOverlay
    {
        get => _localGamingShowOverlay;
        set => SetField(ref _localGamingShowOverlay, value);
    }

    public bool LocalYoutubeShowOverlay
    {
        get => _localYoutubeShowOverlay;
        set => SetField(ref _localYoutubeShowOverlay, value);
    }

    public bool LocalYoutubeRestrictedModeEnabled
    {
        get => _localYoutubeRestrictedModeEnabled;
        set => SetField(ref _localYoutubeRestrictedModeEnabled, value);
    }

    private bool _localYoutubeRestrictedModeEnabled;

    public bool LocalImageShieldEnabled
    {
        get => _localImageShieldEnabled;
        set => SetField(ref _localImageShieldEnabled, value);
    }

    public bool LocalImageShieldTrustedSub
    {
        get => _localImageShieldTrustedSub;
        set => SetField(ref _localImageShieldTrustedSub, value);
    }

    public bool LocalImageShieldSub
    {
        get => _localImageShieldSub;
        set => SetField(ref _localImageShieldSub, value);
    }

    public bool LocalImageShieldRestrictedSub
    {
        get => _localImageShieldRestrictedSub;
        set => SetField(ref _localImageShieldRestrictedSub, value);
    }

    public bool LocalImageShieldFirefox
    {
        get => _localImageShieldFirefox;
        set => SetField(ref _localImageShieldFirefox, value);
    }

    public bool LocalImageShieldChrome
    {
        get => _localImageShieldChrome;
        set => SetField(ref _localImageShieldChrome, value);
    }

    public bool LocalImageShieldEdge
    {
        get => _localImageShieldEdge;
        set => SetField(ref _localImageShieldEdge, value);
    }

    public bool LocalImageShieldBrave
    {
        get => _localImageShieldBrave;
        set => SetField(ref _localImageShieldBrave, value);
    }

    public int LocalImageShieldMinSize
    {
        get => _localImageShieldMinSize;
        set => SetField(ref _localImageShieldMinSize, value);
    }

    public double LocalImageShieldNsfwThreshold
    {
        get => _localImageShieldNsfwThreshold;
        set => SetField(ref _localImageShieldNsfwThreshold, value);
    }

    public int LocalImageShieldMaxPerSecond
    {
        get => _localImageShieldMaxPerSecond;
        set => SetField(ref _localImageShieldMaxPerSecond, value);
    }

    public bool LocalImageShieldChromiumAvailable => Config.ExtensionGuardEnforceChromium;

    public string LocalImageShieldFirefoxStatus => _localImageShieldFirefoxStatus;
    public string LocalImageShieldChromeStatus => _localImageShieldChromeStatus;
    public string LocalImageShieldEdgeStatus => _localImageShieldEdgeStatus;
    public string LocalImageShieldBraveStatus => _localImageShieldBraveStatus;

    private bool _localImageShieldEnabled = true;
    private bool _localImageShieldTrustedSub = true;
    private bool _localImageShieldSub = true;
    private bool _localImageShieldRestrictedSub = true;
    private bool _localImageShieldFirefox = true;
    private bool _localImageShieldChrome;
    private bool _localImageShieldEdge;
    private bool _localImageShieldBrave;
    private int _localImageShieldMinSize = 80;
    private double _localImageShieldNsfwThreshold = 0.45;
    private int _localImageShieldMaxPerSecond = 24;
    private string _localImageShieldFirefoxStatus = string.Empty;
    private string _localImageShieldChromeStatus = string.Empty;
    private string _localImageShieldEdgeStatus = string.Empty;
    private string _localImageShieldBraveStatus = string.Empty;

    public string LocalBlockedUrl
    {
        get => _localBlockedUrl;
        set
        {
            if (SetField(ref _localBlockedUrl, value))
                BlockLocalUrlCommand.RaiseCanExecuteChanged();
        }
    }

    public string LocalBlockedApp
    {
        get => _localBlockedApp;
        set
        {
            if (SetField(ref _localBlockedApp, value))
                BlockLocalAppCommand.RaiseCanExecuteChanged();
        }
    }

    public string LocalKioskManualPath
    {
        get => _localKioskManualPath;
        set
        {
            if (SetField(ref _localKioskManualPath, value))
                AddLocalKioskAppCommand.RaiseCanExecuteChanged();
        }
    }

    public string LocalKioskManualName
    {
        get => _localKioskManualName;
        set => SetField(ref _localKioskManualName, value);
    }

    public string LocalExitPin
    {
        get => _localExitPin;
        set => SetField(ref _localExitPin, value);
    }

    /// <summary>True when an exit PIN is currently set. The PIN itself is never shown back.</summary>
    public bool LocalExitPinIsSet => _exitPin.IsRequired;

    /// <summary>The Sub's nickname used in greetings ("Hi, {name}!"). Editable, unlike the PIN.</summary>
    public string LocalSubDisplayName
    {
        get => _localSubDisplayName;
        set => SetField(ref _localSubDisplayName, value);
    }

    public string LocalExitPinStatusText => LocalExitPinIsSet ? "A PIN is set" : "No PIN set";

    public int LocalScreenshotInterval
    {
        get => _localScreenshotInterval;
        set => SetField(ref _localScreenshotInterval, value);
    }

    public int LocalPunishmentThresholdTrustedToSub
    {
        get => _localPunishmentThresholdTrustedToSub;
        set => SetField(ref _localPunishmentThresholdTrustedToSub, value);
    }

    public int LocalPunishmentThresholdSubToRestricted
    {
        get => _localPunishmentThresholdSubToRestricted;
        set => SetField(ref _localPunishmentThresholdSubToRestricted, value);
    }

    public int LocalPunishmentEscalationHours
    {
        get => _localPunishmentEscalationHours;
        set => SetField(ref _localPunishmentEscalationHours, value);
    }

    public int LocalPunishmentEscalationMinutes
    {
        get => _localPunishmentEscalationMinutes;
        set => SetField(ref _localPunishmentEscalationMinutes, value);
    }

    public int LocalVpnExtensionHours
    {
        get => _localPunishmentExtensions.VpnHours;
        set => SetExtensionHours(v => _localPunishmentExtensions.VpnHours = v, value, nameof(LocalVpnExtensionHours));
    }

    public int LocalVpnExtensionMinutes
    {
        get => _localPunishmentExtensions.VpnMinutes;
        set => SetExtensionMinutes(v => _localPunishmentExtensions.VpnMinutes = v, value, nameof(LocalVpnExtensionMinutes));
    }

    public int LocalBlockedAppExtensionHours
    {
        get => _localPunishmentExtensions.BlockedAppHours;
        set => SetExtensionHours(v => _localPunishmentExtensions.BlockedAppHours = v, value, nameof(LocalBlockedAppExtensionHours));
    }

    public int LocalBlockedAppExtensionMinutes
    {
        get => _localPunishmentExtensions.BlockedAppMinutes;
        set => SetExtensionMinutes(v => _localPunishmentExtensions.BlockedAppMinutes = v, value, nameof(LocalBlockedAppExtensionMinutes));
    }

    public int LocalBypassExtensionHours
    {
        get => _localPunishmentExtensions.BypassHours;
        set => SetExtensionHours(v => _localPunishmentExtensions.BypassHours = v, value, nameof(LocalBypassExtensionHours));
    }

    public int LocalBypassExtensionMinutes
    {
        get => _localPunishmentExtensions.BypassMinutes;
        set => SetExtensionMinutes(v => _localPunishmentExtensions.BypassMinutes = v, value, nameof(LocalBypassExtensionMinutes));
    }

    public int LocalLimitExtensionHours
    {
        get => _localPunishmentExtensions.LimitHours;
        set => SetExtensionHours(v => _localPunishmentExtensions.LimitHours = v, value, nameof(LocalLimitExtensionHours));
    }

    public int LocalLimitExtensionMinutes
    {
        get => _localPunishmentExtensions.LimitMinutes;
        set => SetExtensionMinutes(v => _localPunishmentExtensions.LimitMinutes = v, value, nameof(LocalLimitExtensionMinutes));
    }

    public int LocalStudyExtensionHours
    {
        get => _localPunishmentExtensions.StudyHours;
        set => SetExtensionHours(v => _localPunishmentExtensions.StudyHours = v, value, nameof(LocalStudyExtensionHours));
    }

    public int LocalStudyExtensionMinutes
    {
        get => _localPunishmentExtensions.StudyMinutes;
        set => SetExtensionMinutes(v => _localPunishmentExtensions.StudyMinutes = v, value, nameof(LocalStudyExtensionMinutes));
    }

    public int LocalBlockedSearchExtensionHours
    {
        get => _localPunishmentExtensions.BlockedSearchHours;
        set => SetExtensionHours(v => _localPunishmentExtensions.BlockedSearchHours = v, value, nameof(LocalBlockedSearchExtensionHours));
    }

    public int LocalBlockedSearchExtensionMinutes
    {
        get => _localPunishmentExtensions.BlockedSearchMinutes;
        set => SetExtensionMinutes(v => _localPunishmentExtensions.BlockedSearchMinutes = v, value, nameof(LocalBlockedSearchExtensionMinutes));
    }

    public int LocalTrustRegenPerHour
    {
        get => _localTrustRegenPerHour;
        set => SetField(ref _localTrustRegenPerHour, Math.Clamp(value, 1, 100));
    }

    public int LocalTrustWeightVpn
    {
        get => _localTrustWeightVpn;
        set => SetField(ref _localTrustWeightVpn, Math.Clamp(value, 1, 100));
    }

    public int LocalTrustWeightBypass
    {
        get => _localTrustWeightBypass;
        set => SetField(ref _localTrustWeightBypass, Math.Clamp(value, 1, 100));
    }

    public int LocalTrustWeightBlockedApp
    {
        get => _localTrustWeightBlockedApp;
        set => SetField(ref _localTrustWeightBlockedApp, Math.Clamp(value, 1, 100));
    }

    public int LocalTrustWeightBlockedSearch
    {
        get => _localTrustWeightBlockedSearch;
        set => SetField(ref _localTrustWeightBlockedSearch, Math.Clamp(value, 1, 100));
    }

    public int LocalTrustWeightStudy
    {
        get => _localTrustWeightStudy;
        set => SetField(ref _localTrustWeightStudy, Math.Clamp(value, 1, 100));
    }

    public int LocalTrustWeightLimit
    {
        get => _localTrustWeightLimit;
        set => SetField(ref _localTrustWeightLimit, Math.Clamp(value, 1, 100));
    }

    private void SetExtensionHours(Action<int> setter, int value, string propertyName)
    {
        setter(Math.Clamp(value, 0, 720));
        OnPropertyChanged(propertyName);
    }

    private void SetExtensionMinutes(Action<int> setter, int value, string propertyName)
    {
        setter(Math.Clamp(value, 0, 59));
        OnPropertyChanged(propertyName);
    }

    public bool LocalPunishmentEnabled
    {
        get => _localPunishmentEnabled;
        set => SetField(ref _localPunishmentEnabled, value);
    }

    public bool LocalInfractionVpnEnabled
    {
        get => _localInfractionVpnEnabled;
        set => SetField(ref _localInfractionVpnEnabled, value);
    }

    public bool LocalInfractionBlockedAppEnabled
    {
        get => _localInfractionBlockedAppEnabled;
        set => SetField(ref _localInfractionBlockedAppEnabled, value);
    }

    public bool LocalInfractionBypassEnabled
    {
        get => _localInfractionBypassEnabled;
        set => SetField(ref _localInfractionBypassEnabled, value);
    }

    public bool LocalInfractionLimitEnabled
    {
        get => _localInfractionLimitEnabled;
        set => SetField(ref _localInfractionLimitEnabled, value);
    }

    public bool LocalInfractionStudyEnabled
    {
        get => _localInfractionStudyEnabled;
        set => SetField(ref _localInfractionStudyEnabled, value);
    }

    public bool LocalInfractionBlockedSearchEnabled
    {
        get => _localInfractionBlockedSearchEnabled;
        set => SetField(ref _localInfractionBlockedSearchEnabled, value);
    }

    private bool _localInfractionVpnEnabled = true;
    private bool _localInfractionBlockedAppEnabled = true;
    private bool _localInfractionBypassEnabled = true;
    private bool _localInfractionLimitEnabled = true;
    private bool _localInfractionStudyEnabled = true;
    private bool _localInfractionBlockedSearchEnabled = true;

    public bool LocalBlockTaskManager
    {
        get => _localBlockTaskManager;
        set => SetField(ref _localBlockTaskManager, value);
    }

    public bool LocalVpnShield
    {
        get => _localVpnShield;
        set => SetField(ref _localVpnShield, value);
    }

    public bool LocalBlockRegistryEditor
    {
        get => _localBlockRegistryEditor;
        set => SetField(ref _localBlockRegistryEditor, value);
    }

    public bool LocalBlockCommandPrompt
    {
        get => _localBlockCommandPrompt;
        set => SetField(ref _localBlockCommandPrompt, value);
    }

    public bool LocalBlockPowerShell
    {
        get => _localBlockPowerShell;
        set => SetField(ref _localBlockPowerShell, value);
    }

    public bool LocalBlockSystemConfig
    {
        get => _localBlockSystemConfig;
        set => SetField(ref _localBlockSystemConfig, value);
    }

    public bool LocalBlockControlPanel
    {
        get => _localBlockControlPanel;
        set => SetField(ref _localBlockControlPanel, value);
    }

    public bool LocalBlockProcessTools
    {
        get => _localBlockProcessTools;
        set => SetField(ref _localBlockProcessTools, value);
    }

    public bool LocalBlockProcessKillers
    {
        get => _localBlockProcessKillers;
        set => SetField(ref _localBlockProcessKillers, value);
    }

    public bool LocalKioskMode
    {
        get => _localKioskMode;
        set => SetField(ref _localKioskMode, value);
    }

    public bool LocalStudyEnabled
    {
        get => _localStudyEnabled;
        set => SetField(ref _localStudyEnabled, value);
    }

    public string LocalStudyStart
    {
        get => _localStudyStart;
        set => SetField(ref _localStudyStart, value);
    }

    public string LocalStudyEnd
    {
        get => _localStudyEnd;
        set => SetField(ref _localStudyEnd, value);
    }

    public string LocalStudyDaysText
    {
        get => _localStudyDaysText;
        set => SetField(ref _localStudyDaysText, value);
    }

    public bool LocalStudyBlockGames
    {
        get => _localStudyBlockGames;
        set => SetField(ref _localStudyBlockGames, value);
    }

    public bool LocalStudyBlockYoutube
    {
        get => _localStudyBlockYoutube;
        set => SetField(ref _localStudyBlockYoutube, value);
    }

    public bool LocalStudyBlockDistractingSites
    {
        get => _localStudyBlockDistractingSites;
        set => SetField(ref _localStudyBlockDistractingSites, value);
    }

    public bool LocalStudyBlockDistractingApps
    {
        get => _localStudyBlockDistractingApps;
        set => SetField(ref _localStudyBlockDistractingApps, value);
    }

    private bool _localStudyBlockGames = true;
    private bool _localStudyBlockYoutube = true;
    private bool _localStudyBlockDistractingSites = true;
    private bool _localStudyBlockDistractingApps = true;

    private bool _localSettingsPinUnlocked;

    private static bool IsLocalSettingsEditContext(string context) =>
        context is "local settings save"
            or "active supervision mode"
            or "block site"
            or "block app"
            or "clear punishment"
            or "web category filter";

    private void ClearLocalSettingsPinSession() => _localSettingsPinUnlocked = false;

    private bool TryAuthorizeRestrictionChange(string context)
    {
        if (_localSettingsPinUnlocked && IsLocalMode && IsLocalAreaPage && IsLocalSettingsEditContext(context))
            return true;

        return TryAuthorizeProtectedAction(context);
    }

    private async Task ToggleLocalModeAsync()
    {
        var pinContext = IsLocalMode ? "exit local mode" : "enter local mode";
        if (!TryAuthorizeRestrictionChange(pinContext))
            return;

        if (!IsLocalMode)
        {
            _localSettings.SeedFromRuntimeIfEmpty();
            _localMode.Enable();
            AddLog(LocalModeCopy.EnabledLog);
            return;
        }

        _isLocalSyncing = true;
        OnPropertyChanged(nameof(IsLocalSyncing));
        ToggleLocalModeCommand.RaiseCanExecuteChanged();
        AddLog(LocalModeCopy.SyncingLog);

        _localMode.Disable();

        var synced = false;
        if (IsEnrolled)
            synced = await _host.ResyncFromServerAsync(CancellationToken.None).ConfigureAwait(true);

        _isLocalSyncing = false;
        OnPropertyChanged(nameof(IsLocalSyncing));
        ToggleLocalModeCommand.RaiseCanExecuteChanged();

        if (IsEnrolled && !synced)
        {
            _localMode.Enable();
            LocalSyncWarning = LocalModeCopy.SyncFailedKeepLocal;
            AddLog(LocalModeCopy.SyncFailedKeepLocal);
            StatusText = UiCopy.StatusConnectionIssue;
            return;
        }

        LocalSyncWarning = string.Empty;
        AddLog(IsEnrolled ? LocalModeCopy.SyncedLog : LocalModeCopy.DisabledLog);
        if (IsEnrolled)
            StatusText = synced ? UiCopy.StatusSupervisionActive : UiCopy.StatusConnectionIssue;

        if (IsLocalAreaPage)
            NavigateHome();
    }

    private void OnLocalModeChanged() => PostOnUi(() =>
    {
        OnPropertyChanged(nameof(IsLocalMode));
        NotifyWidgetScreenTimeChanged();
        OnPropertyChanged(nameof(LocalModeButtonLabel));
        OnPropertyChanged(nameof(ShowEnrollmentOverlay));
        NavigateToLocalSettingsCommand.RaiseCanExecuteChanged();
        OpenLocalSectionCommand.RaiseCanExecuteChanged();
        SaveLocalSectionCommand.RaiseCanExecuteChanged();
        LocalLockScreenCommand.RaiseCanExecuteChanged();
        SaveLocalActiveModeCommand.RaiseCanExecuteChanged();
        ToggleLocalModeCommand.RaiseCanExecuteChanged();
        // These RelayCommands have no CommandManager.RequerySuggested hookup, so a Button
        // bound to one before local mode was ever enabled (the whole panel is always in the
        // visual tree — see MainWindow.xaml's LocalSettingsPanel, just Visibility-toggled)
        // stays stuck at its first-evaluated CanExecute forever unless raised explicitly here.
        SelectLocalEditingModeCommand.RaiseCanExecuteChanged();
        SelectLocalActiveModeCommand.RaiseCanExecuteChanged();
        ClearLocalPunishmentCommand.RaiseCanExecuteChanged();
        RefreshLocalKioskAppsCommand.RaiseCanExecuteChanged();
        AddLocalKioskAppCommand.RaiseCanExecuteChanged();
        BrowseLocalKioskAppCommand.RaiseCanExecuteChanged();
        CopyImageShieldDiagnosticCommand.RaiseCanExecuteChanged();
        AddLocalAppTimeLimitCommand.RaiseCanExecuteChanged();
        RemoveLocalAppTimeLimitCommand.RaiseCanExecuteChanged();
        BrowseLocalCustomGameCommand.RaiseCanExecuteChanged();
        AddLocalCustomGameCommand.RaiseCanExecuteChanged();
        RemoveLocalCustomGameCommand.RaiseCanExecuteChanged();
        RemoveLocalExitPinCommand.RaiseCanExecuteChanged();

        if (IsLocalMode)
        {
            BootstrapLocalSupervision();
            _localSettings.ApplyActiveMode();
            OnPropertyChanged(nameof(ExitPinRequired));
            StatusText = LocalModeCopy.StatusLabel;
        }
        else if (!IsEnrolled)
        {
            _screenTime.Stop();
            _gaming.Stop();
            _appTimeLimits.Stop();
            _appTimeLimits.ApplyLimits(null);
            _youtube.Stop();
            _punishment.Stop();
            StatusText = UiCopy.StatusOffline;
        }

        if (!IsLocalMode)
            ClearLocalSettingsPinSession();

        NotifySupervisionPresentationChanged();
    });

    private void BootstrapLocalSupervision()
    {
        EnsureAutoStartDefaultWhenSupervised();
        Task.Run(Security.SecurityActivation.EnsureActive);
        _screenTime.Start();
        _gaming.Start();
        _appTimeLimits.Start();
        _youtube.Start();
        _punishment.Start();
        ReconcileImageShield();
        _bedtime.SyncLockState();
    }

    private void NavigateToLocalSettings()
    {
        if (!IsLocalMode)
            return;

        if (!TryAuthorizeProtectedAction("local settings"))
            return;

        _localSettingsPinUnlocked = true;
        OpenLocalHub();
    }

    private void OpenLocalHub()
    {
        _localSectionKey = string.Empty;
        LocalEditingModeSlug = _localSettings.ActiveModeSlug;
        LocalActiveModeSlug = _localSettings.ActiveModeSlug;
        CurrentPage = DashboardPage.LocalHub;
        NotifyLocalPageChanged();
    }

    private void OpenLocalSection(string? key)
    {
        if (!IsLocalMode || string.IsNullOrWhiteSpace(key))
            return;

        _localSectionKey = key.Trim().ToLowerInvariant();
        LocalEditingModeSlug = _localSettings.ActiveModeSlug;
        LocalActiveModeSlug = _localSettings.ActiveModeSlug;
        LoadSectionFormFromCatalog();
        if (_localSectionKey == "kiosk")
            RefreshLocalKioskApps();
        if (_localSectionKey == "playtime")
            LoadLocalCustomGames();
        if (_localSectionKey == "blocklist")
            LoadLocalWebCategories();
        CurrentPage = DashboardPage.LocalSection;
        NotifyLocalPageChanged();
    }

    private void NavigateBack()
    {
        if (CurrentPage == DashboardPage.LocalSection)
        {
            _localSectionKey = string.Empty;
            CurrentPage = DashboardPage.LocalHub;
            NotifyLocalPageChanged();
            return;
        }

        NavigateHome();
    }

    private void NotifyLocalPageChanged()
    {
        OnPropertyChanged(nameof(IsLocalHubPage));
        OnPropertyChanged(nameof(IsLocalSectionPage));
        OnPropertyChanged(nameof(IsLocalAreaPage));
        OnPropertyChanged(nameof(LocalSectionTitle));
        OnPropertyChanged(nameof(LocalSectionSubtitle));
        OnPropertyChanged(nameof(LocalSectionUsesModePicker));
        OnPropertyChanged(nameof(IsLocalSupervisionSection));
        OnPropertyChanged(nameof(IsLocalKioskSection));
        OnPropertyChanged(nameof(IsLocalBedtimeSection));
        OnPropertyChanged(nameof(IsLocalPlaytimeSection));
        OnPropertyChanged(nameof(IsLocalAppLimitsSection));
        OnPropertyChanged(nameof(IsLocalYoutubeSection));
        OnPropertyChanged(nameof(IsLocalStudySection));
        OnPropertyChanged(nameof(IsLocalDisciplineSection));
        OnPropertyChanged(nameof(IsLocalImageShieldSection));
        OnPropertyChanged(nameof(IsLocalSecuritySection));
        OnPropertyChanged(nameof(IsLocalBlocklistSection));
        OnPropertyChanged(nameof(IsLocalControlSection));
        OnPropertyChanged(nameof(IsLocalScreenshotsSection));
        OnPropertyChanged(nameof(IsLocalAppearanceSection));
        OnPropertyChanged(nameof(HeaderCenterTitle));
        OnPropertyChanged(nameof(HeaderCenterSubtitle));
        OnPropertyChanged(nameof(HomeGreetingTitle));
        OnPropertyChanged(nameof(HomeGreetingSubtitle));
        OnPropertyChanged(nameof(IsSettingsPage));
        NavigateBackCommand.RaiseCanExecuteChanged();
    }

    private void LoadSectionFormFromCatalog()
    {
        var rules = _localSettings.RulesFor(LocalEditingModeSlug);
        LocalScreenTimeMinutes = rules.ScreenTimeDailyLimitMinutes;
        LocalBedtimeEnabled = rules.BedtimeEnabled;
        LocalBedtimeTime = rules.BedtimeTime;
        LocalWakeTime = rules.WakeTime;
        SyncBedtimeWeekly(
            LocalBedtimeWeekly,
            rules.BedtimeWeekly,
            rules.BedtimeTime,
            rules.WakeTime);
        LocalGamingLimitMinutes = rules.GamingDailyLimitMinutes;
        LocalYoutubeLimitMinutes = rules.YoutubeDailyLimitMinutes;
        LocalGamingShowOverlay = rules.GamingShowOverlay;
        LocalYoutubeShowOverlay = rules.YoutubeShowOverlay;
        LocalYoutubeRestrictedModeEnabled = _localSettings.Catalog.YouTubeRestrictedModeEnabled;
        LocalStudyEnabled = rules.StudyEnabled;
        LocalStudyStart = rules.StudyStart;
        LocalStudyEnd = rules.StudyEnd;
        LocalStudyDaysText = string.Join(",", rules.StudyDays);
        LocalStudyBlockGames = rules.StudyBlockGames;
        LocalStudyBlockYoutube = rules.StudyBlockYoutube;
        LocalStudyBlockDistractingSites = rules.StudyBlockDistractingSites;
        LocalStudyBlockDistractingApps = rules.StudyBlockDistractingApps;
        SyncStudyWeekly(
            LocalStudyWeekly,
            rules.StudyWeekly,
            rules.StudyStart,
            rules.StudyEnd);
        LocalBlockTaskManager = rules.BlockTaskManager;
        LocalVpnShield = rules.VpnShield;
        LocalBlockRegistryEditor = rules.BlockRegistryEditor;
        LocalBlockCommandPrompt = rules.BlockCommandPrompt;
        LocalBlockPowerShell = rules.BlockPowerShell;
        LocalBlockSystemConfig = rules.BlockSystemConfig;
        LocalBlockControlPanel = rules.BlockControlPanel;
        LocalBlockProcessTools = rules.BlockProcessTools;
        LocalBlockProcessKillers = rules.BlockProcessKillers;
        LocalKioskMode = rules.KioskMode;

        SyncWeeklyLimits(LocalScreenWeekly, rules.ScreenTimeWeekly, rules.ScreenTimeDailyLimitMinutes);
        SyncWeeklyLimits(LocalGamingWeekly, rules.GamingWeekly, rules.GamingDailyLimitMinutes);
        SyncWeeklyLimits(LocalYoutubeWeekly, rules.YoutubeWeekly, rules.YoutubeDailyLimitMinutes);
        LoadLocalAppTimeLimits(rules.AppTimeLimits);

        var catalog = _localSettings.Catalog;
        LocalBlueLightFilterEnabled = catalog.BlueLightFilterEnabled;
        LocalImageShieldEnabled = catalog.ImageShieldEnabled;
        // Never repopulate the PIN — the field is "set/change" only. Blank means "keep".
        LocalExitPin = string.Empty;
        RaiseExitPinState();
        // Unlike the PIN, the nickname isn't a secret — show the current value so it's
        // editable in place.
        LocalSubDisplayName = _subProfile.DisplayName ?? string.Empty;
        LocalWidgetRemindersEnabled = catalog.DesktopWidgetRemindersEnabled;
        LocalWidgetReminderFrequencyMinutes = Math.Clamp(catalog.DesktopWidgetReminderFrequencyMinutes, 1, 180);
        LocalShowDesktopWidget = catalog.ShowDesktopWidget;
        LocalShowGamingTimer = catalog.ShowGamingTimer;
        LocalShowYoutubeTimer = catalog.ShowYoutubeTimer;
        LocalStyledNewTabPage = catalog.StyledNewTabPage;
        LocalShowWebsiteBadge = catalog.ShowWebsiteBadge;
        LocalScreenshotInterval = catalog.ScreenshotIntervalMinutes;
        LocalPunishmentEnabled = catalog.PunishmentEnabled;
        LocalPunishmentThresholdTrustedToSub = catalog.PunishmentThresholdTrustedToSub > 0
            ? catalog.PunishmentThresholdTrustedToSub
            : catalog.PunishmentInfractionThreshold;
        LocalPunishmentThresholdSubToRestricted = catalog.PunishmentThresholdSubToRestricted > 0
            ? catalog.PunishmentThresholdSubToRestricted
            : catalog.PunishmentInfractionThreshold;
        LocalPunishmentEscalationHours = catalog.PunishmentEscalationHours;
        LocalPunishmentEscalationMinutes = catalog.PunishmentEscalationMinutes;
        _localPunishmentExtensions = catalog.PunishmentExtensions ?? PunishmentExtensionCatalog.CreateDefaults();
        LocalInfractionVpnEnabled = catalog.InfractionVpnAttempt;
        LocalInfractionBlockedAppEnabled = catalog.InfractionBlockedAppRepeated;
        LocalInfractionBypassEnabled = catalog.InfractionBypassAttempt;
        LocalInfractionLimitEnabled = catalog.InfractionLimitIgnored;
        LocalInfractionStudyEnabled = catalog.InfractionStudyTimeViolation;
        LocalInfractionBlockedSearchEnabled = catalog.InfractionBlockedSearch;
        LocalTrustRegenPerHour = catalog.TrustRegenPerHour;
        LocalTrustWeightVpn = catalog.TrustWeightVpn;
        LocalTrustWeightBypass = catalog.TrustWeightBypass;
        LocalTrustWeightBlockedApp = catalog.TrustWeightBlockedApp;
        LocalTrustWeightBlockedSearch = catalog.TrustWeightBlockedSearch;
        LocalTrustWeightStudy = catalog.TrustWeightStudy;
        LocalTrustWeightLimit = catalog.TrustWeightLimit;
        LoadLocalImageShieldFields();
        OnPropertyChanged(nameof(LocalVpnExtensionHours));
        OnPropertyChanged(nameof(LocalVpnExtensionMinutes));
        OnPropertyChanged(nameof(LocalBlockedAppExtensionHours));
        OnPropertyChanged(nameof(LocalBlockedAppExtensionMinutes));
        OnPropertyChanged(nameof(LocalBypassExtensionHours));
        OnPropertyChanged(nameof(LocalBypassExtensionMinutes));
        OnPropertyChanged(nameof(LocalLimitExtensionHours));
        OnPropertyChanged(nameof(LocalLimitExtensionMinutes));
        OnPropertyChanged(nameof(LocalStudyExtensionHours));
        OnPropertyChanged(nameof(LocalStudyExtensionMinutes));
        OnPropertyChanged(nameof(LocalBlockedSearchExtensionHours));
        OnPropertyChanged(nameof(LocalBlockedSearchExtensionMinutes));
    }

    private void SaveLocalSection()
    {
        if (!IsLocalMode)
            return;

        if (!TryAuthorizeRestrictionChange("local settings save"))
            return;

        switch (_localSectionKey)
        {
            case "supervision":
                SavePerModeRules(rules =>
                {
                    rules.ScreenTimeDailyLimitMinutes = Math.Max(1, LocalScreenTimeMinutes);
                    rules.ScreenTimeWeekly = CollectWeeklyOverrides(LocalScreenWeekly);
                    rules.BlockTaskManager = LocalBlockTaskManager;
                    rules.VpnShield = LocalVpnShield;
                    rules.BlockRegistryEditor = LocalBlockRegistryEditor;
                    rules.BlockCommandPrompt = LocalBlockCommandPrompt;
                    rules.BlockPowerShell = LocalBlockPowerShell;
                    rules.BlockSystemConfig = LocalBlockSystemConfig;
                    rules.BlockControlPanel = LocalBlockControlPanel;
                    rules.BlockProcessTools = LocalBlockProcessTools;
                    rules.BlockProcessKillers = LocalBlockProcessKillers;
                    rules.KioskMode = LocalKioskMode;
                });
                _localSettings.ApplyActiveMode();
                RefreshRestrictions();
                break;

            case "bedtime":
                _localSettings.Catalog.BlueLightFilterEnabled = LocalBlueLightFilterEnabled;
                _localSettings.Persist();
                SavePerModeRules(rules =>
                {
                    rules.BedtimeEnabled = LocalBedtimeEnabled;
                    rules.BedtimeTime = LocalBedtimeTime;
                    rules.WakeTime = LocalWakeTime;
                    rules.BedtimeWeekly = CollectBedtimeWeeklyOverrides(LocalBedtimeWeekly);
                });
                ApplyBedtimeBlueLightFilterFromCatalog();
                break;

            case "playtime":
                SavePerModeRules(rules =>
                {
                    rules.GamingDailyLimitMinutes = Math.Max(0, LocalGamingLimitMinutes);
                    rules.GamingWeekly = CollectWeeklyOverrides(LocalGamingWeekly);
                    rules.GamingShowOverlay = LocalGamingShowOverlay;
                });
                break;

            case "app_limits":
                SavePerModeRules(rules =>
                {
                    rules.AppTimeLimits = CollectLocalAppTimeLimits();
                });
                break;

            case "youtube":
                _localSettings.Catalog.YouTubeRestrictedModeEnabled = LocalYoutubeRestrictedModeEnabled;
                _localSettings.Persist();
                _youtube.SetRestrictedModeEnabled(LocalYoutubeRestrictedModeEnabled);
                SavePerModeRules(rules =>
                {
                    rules.YoutubeDailyLimitMinutes = Math.Max(0, LocalYoutubeLimitMinutes);
                    rules.YoutubeWeekly = CollectWeeklyOverrides(LocalYoutubeWeekly);
                    rules.YoutubeShowOverlay = LocalYoutubeShowOverlay;
                });
                ReconcileYoutubeRestrictedMode();
                return;

            case "study":
                SavePerModeRules(rules =>
                {
                    rules.StudyEnabled = LocalStudyEnabled;
                    rules.StudyStart = LocalStudyStart;
                    rules.StudyEnd = LocalStudyEnd;
                    rules.StudyDays = ParseStudyDays(LocalStudyDaysText);
                    rules.StudyWeekly = CollectStudyWeeklyOverrides(LocalStudyWeekly);
                    rules.StudyBlockGames = LocalStudyBlockGames;
                    rules.StudyBlockYoutube = LocalStudyBlockYoutube;
                    rules.StudyBlockDistractingSites = LocalStudyBlockDistractingSites;
                    rules.StudyBlockDistractingApps = LocalStudyBlockDistractingApps;
                });
                break;

            case "kiosk":
                PersistKioskApprovals();
                AddLog(LocalModeCopy.KioskAppsSavedLog);
                return;

            case "discipline":
                _localSettings.Catalog.PunishmentEnabled = LocalPunishmentEnabled;
                _localSettings.Catalog.PunishmentThresholdTrustedToSub = Math.Max(1, LocalPunishmentThresholdTrustedToSub);
                _localSettings.Catalog.PunishmentThresholdSubToRestricted = Math.Max(1, LocalPunishmentThresholdSubToRestricted);
                _localSettings.Catalog.PunishmentEscalationHours = Math.Max(0, LocalPunishmentEscalationHours);
                _localSettings.Catalog.PunishmentEscalationMinutes = Math.Clamp(LocalPunishmentEscalationMinutes, 0, 59);
                _localSettings.Catalog.PunishmentExtensions = _localPunishmentExtensions;
                _localSettings.Catalog.InfractionVpnAttempt = LocalInfractionVpnEnabled;
                _localSettings.Catalog.InfractionBlockedAppRepeated = LocalInfractionBlockedAppEnabled;
                _localSettings.Catalog.InfractionBypassAttempt = LocalInfractionBypassEnabled;
                _localSettings.Catalog.InfractionLimitIgnored = LocalInfractionLimitEnabled;
                _localSettings.Catalog.InfractionStudyTimeViolation = LocalInfractionStudyEnabled;
                _localSettings.Catalog.InfractionBlockedSearch = LocalInfractionBlockedSearchEnabled;
                _localSettings.Catalog.TrustRegenPerHour = LocalTrustRegenPerHour;
                _localSettings.Catalog.TrustWeightVpn = LocalTrustWeightVpn;
                _localSettings.Catalog.TrustWeightBypass = LocalTrustWeightBypass;
                _localSettings.Catalog.TrustWeightBlockedApp = LocalTrustWeightBlockedApp;
                _localSettings.Catalog.TrustWeightBlockedSearch = LocalTrustWeightBlockedSearch;
                _localSettings.Catalog.TrustWeightStudy = LocalTrustWeightStudy;
                _localSettings.Catalog.TrustWeightLimit = LocalTrustWeightLimit;
                _localSettings.SaveCatalog();
                _localSettings.ApplyGlobalSettings();
                break;

            case "image_shield":
                _localSettings.Catalog.ImageShieldEnabled = LocalImageShieldEnabled;
                _localSettings.Catalog.ImageShieldPerMode = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentModeSlugs.TrustedSub] = LocalImageShieldTrustedSub,
                    [AgentModeSlugs.Sub] = LocalImageShieldSub,
                    [AgentModeSlugs.RestrictedSub] = LocalImageShieldRestrictedSub,
                };
                _localSettings.Catalog.ImageShieldPerBrowser = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    [ImageShieldBrowserKeys.Firefox] = LocalImageShieldFirefox,
                    [ImageShieldBrowserKeys.Chrome] = LocalImageShieldChrome,
                    [ImageShieldBrowserKeys.Edge] = LocalImageShieldEdge,
                    [ImageShieldBrowserKeys.Brave] = LocalImageShieldBrave,
                };
                _localSettings.Catalog.ImageShieldMinSize = Math.Clamp(LocalImageShieldMinSize, 16, 1024);
                _localSettings.Catalog.ImageShieldNsfwThreshold =
                    Math.Clamp(LocalImageShieldNsfwThreshold, 0.1, 0.99);
                _localSettings.Catalog.ImageShieldMaxPerSecond = Math.Clamp(LocalImageShieldMaxPerSecond, 1, 60);
                _localSettings.SaveCatalog();
                _localSettings.ApplyGlobalSettings();
                _imageShieldSignature = null;
                ReconcileImageShield();
                RefreshLocalImageShieldStatus();
                break;

            case "security":
                // Blank = keep the current PIN (the field is never pre-filled). To remove a
                // PIN the parent uses the explicit Remove command.
                if (!string.IsNullOrWhiteSpace(LocalExitPin))
                {
                    var pin = LocalExitPin.Trim();
                    if (!ExitPinService.IsValidFormat(pin))
                    {
                        AddLog("Exit PIN must be 6â€“8 digits.");
                        return;
                    }

                    _exitPin.SetPin(pin);
                    LocalExitPin = string.Empty;
                    RaiseExitPinState();
                }

                var nickname = LocalSubDisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(nickname) && !_subProfile.TrySetDisplayName(nickname))
                {
                    AddLog("Nickname must include at least one letter or number.");
                    return;
                }

                _localSettings.SaveCatalog();
                OnPropertyChanged(nameof(ExitPinRequired));
                break;

            case "appearance":
                _localSettings.Catalog.ShowDesktopWidget = LocalShowDesktopWidget;
                _localSettings.Catalog.ShowGamingTimer = LocalShowGamingTimer;
                _localSettings.Catalog.ShowYoutubeTimer = LocalShowYoutubeTimer;
                _localSettings.Catalog.StyledNewTabPage = LocalStyledNewTabPage;
                _localSettings.Catalog.ShowWebsiteBadge = LocalShowWebsiteBadge;
                _localSettings.Catalog.DesktopWidgetRemindersEnabled = LocalWidgetRemindersEnabled;
                LocalWidgetReminderFrequencyMinutes = Math.Clamp(LocalWidgetReminderFrequencyMinutes, 1, 180);
                _localSettings.Catalog.DesktopWidgetReminderFrequencyMinutes = LocalWidgetReminderFrequencyMinutes;
                _localSettings.SaveCatalog();
                SyncWidgetPromptScheduler(restartCountdown: true);
                OnPropertyChanged(nameof(DesktopWidgetVisible));
                break;

            case "screenshots":
                _localSettings.Catalog.ScreenshotIntervalMinutes = Math.Max(1, LocalScreenshotInterval);
                _localSettings.Persist();
                AddLog("Screenshot interval saved — restart the agent to apply.");
                return;
        }

        OnPropertyChanged(nameof(BedtimeLabel));
        OnPropertyChanged(nameof(BedtimeCardLabel));
        OnScreenTimeElapsed();
        RefreshRestrictions();
        AddLog(LocalModeCopy.SectionSavedLog);
    }

    private void ApplyBedtimeBlueLightFilterFromCatalog()
    {
        var current = _bedtime.Settings;
        var enabled = _localSettings.Catalog.BlueLightFilterEnabled;
        if (current.BlueLightFilterEnabled == enabled)
            return;

        _bedtime.Update(new BedtimeSettings
        {
            Enabled = current.Enabled,
            Time = current.Time,
            WakeTime = current.WakeTime,
            Weekly = current.Weekly,
            BlueLightFilterEnabled = enabled,
        });
    }

    private void SavePerModeRules(Action<LocalPerModeRuleSet> mutate)
    {
        var rules = _localSettings.RulesFor(LocalEditingModeSlug);
        mutate(rules);
        _localSettings.SaveRulesFor(LocalEditingModeSlug, rules);
    }

    /// <summary>
    /// Wires a kiosk app row's approve toggle to persist immediately — there is no separate
    /// Save button for this section, flipping the switch is the commit. Deferred via PostOnUi
    /// so the collection rebuild inside PersistKioskApprovals doesn't mutate LocalKioskApps
    /// while still inside the CheckBox's own click/binding dispatch.
    /// </summary>
    private LocalKioskAppItem TrackKioskAppItem(LocalKioskAppItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalKioskAppItem.IsApproved))
                PostOnUi(PersistKioskApprovals);
        };
        return item;
    }

    /// <summary>
    /// Commits the current checked state of <see cref="LocalKioskApps"/> to the registry that
    /// the kiosk launcher actually reads, then re-syncs the list.
    /// </summary>
    private void PersistKioskApprovals()
    {
        var approvedApps = LocalKioskApps
            .Where(app => app.IsApproved && KioskAppRegistry.IsValidPath(app.Path) && app.IsInstalled)
            .Select(app => new KioskApp(app.Name, app.Path, app.Args, app.Icon))
            .ToList();
        _kioskApps.SetApprovedApps(approvedApps);
        RefreshLocalKioskApps();
    }

    private void RefreshLocalKioskApps()
    {
        _kioskApps.MergeNewlyDiscoveredDefaults();

        var approvedPaths = new HashSet<string>(
            _kioskApps.Apps.Select(app => app.NormalizedPath),
            StringComparer.OrdinalIgnoreCase);

        var discovered = KioskAppDiscovery.DiscoverAll();
        var items = new List<LocalKioskAppItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in discovered)
        {
            var normalized = KioskApp.NormalizePath(app.Path);
            seenPaths.Add(normalized);
            items.Add(TrackKioskAppItem(new LocalKioskAppItem
            {
                CatalogId = app.CatalogId,
                Name = app.Name,
                Path = app.Path,
                IconImage = ExecutableIconService.GetForPath(app.Path),
                Icon = app.Icon,
                Args = app.Args,
                IsApproved = approvedPaths.Contains(normalized),
            }));
        }

        foreach (var app in _kioskApps.Apps)
        {
            if (seenPaths.Contains(app.NormalizedPath))
                continue;

            seenPaths.Add(app.NormalizedPath);
            items.Add(TrackKioskAppItem(new LocalKioskAppItem
            {
                Name = app.Name,
                Path = app.Path,
                IconImage = ExecutableIconService.GetForPath(app.Path),
                Icon = string.IsNullOrWhiteSpace(app.Icon) ? "\U0001F4E6" : app.Icon!,
                Args = app.Args,
                IsApproved = true,
            }));
        }

        foreach (var manual in LocalKioskApps.Where(item => string.IsNullOrEmpty(item.CatalogId)))
        {
            if (string.IsNullOrWhiteSpace(manual.Path))
                continue;

            var normalized = KioskApp.NormalizePath(manual.Path);
            if (seenPaths.Contains(normalized))
                continue;

            seenPaths.Add(normalized);
            items.Add(TrackKioskAppItem(new LocalKioskAppItem
            {
                Name = manual.Name,
                Path = manual.Path,
                IconImage = ExecutableIconService.GetForPath(manual.Path),
                Icon = manual.Icon,
                Args = manual.Args,
                IsApproved = manual.IsApproved,
            }));
        }

        LocalKioskApps.Clear();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            LocalKioskApps.Add(item);

        OnPropertyChanged(nameof(HasLocalKioskApps));
    }

    private void BrowseLocalKioskApp()
    {
        if (!IsLocalMode)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalModeCopy.KioskAppsAddTitle,
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        LocalKioskManualPath = dialog.FileName;
    }

    private void AddLocalKioskApp()
    {
        if (!IsLocalMode || string.IsNullOrWhiteSpace(LocalKioskManualPath))
            return;

        string path;
        try
        {
            path = Path.GetFullPath(LocalKioskManualPath.Trim());
        }
        catch
        {
            AddLog(LocalModeCopy.KioskAppsInvalidPath);
            return;
        }

        if (!KioskAppRegistry.IsValidPath(path) || !File.Exists(path))
        {
            AddLog(LocalModeCopy.KioskAppsInvalidPath);
            return;
        }

        var normalized = KioskApp.NormalizePath(path);
        var existing = LocalKioskApps.FirstOrDefault(app =>
            string.Equals(KioskApp.NormalizePath(app.Path), normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Already in the list (likely auto-detected but not yet checked) — approve it
            // instead of silently no-oping, since clicking Add visibly did nothing otherwise.
            existing.IsApproved = true;
            LocalKioskManualPath = string.Empty;
            LocalKioskManualName = string.Empty;
            PersistKioskApprovals();
            AddLog(string.Format(LocalModeCopy.KioskAppsAddedLog, existing.Name));
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(LocalKioskManualName)
            ? AppDisplayNames.Resolve(Path.GetFileName(path))
            : LocalKioskManualName.Trim();

        LocalKioskApps.Add(new LocalKioskAppItem
        {
            Name = displayName,
            Path = path,
            IconImage = ExecutableIconService.GetForPath(path),
            Icon = "\U0001F4E6",
            IsApproved = true,
        });

        LocalKioskManualPath = string.Empty;
        LocalKioskManualName = string.Empty;
        OnPropertyChanged(nameof(HasLocalKioskApps));
        PersistKioskApprovals();
        AddLog(string.Format(LocalModeCopy.KioskAppsAddedLog, displayName));
    }

    private static Dictionary<string, LocalStudyDayOverride>? CollectStudyWeeklyOverrides(
        IEnumerable<LocalDayStudyItem> items)
    {
        var weekly = items
            .Where(i => i.OverrideEnabled)
            .ToDictionary(
                i => i.DayKey,
                i => new LocalStudyDayOverride
                {
                    Enabled = true,
                    StartTime = (i.StartTime ?? string.Empty).Trim(),
                    EndTime = (i.EndTime ?? string.Empty).Trim(),
                },
                StringComparer.OrdinalIgnoreCase);
        return weekly.Count > 0 ? weekly : null;
    }

    private static void SyncStudyWeekly(
        ObservableCollection<LocalDayStudyItem> items,
        Dictionary<string, LocalStudyDayOverride>? weekly,
        string defaultStart,
        string defaultEnd)
    {
        if (items.Count == 0)
        {
            foreach (var (key, label) in WeekDays)
            {
                items.Add(new LocalDayStudyItem
                {
                    DayKey = key,
                    DayLabel = label,
                    StartTime = defaultStart,
                    EndTime = defaultEnd,
                });
            }
        }

        foreach (var item in items)
        {
            var hasOverride = weekly?.ContainsKey(item.DayKey) == true;
            item.OverrideEnabled = hasOverride;
            if (!hasOverride)
            {
                item.StartTime = defaultStart;
                item.EndTime = defaultEnd;
                continue;
            }

            var day = weekly![item.DayKey];
            item.StartTime = day.StartTime ?? defaultStart;
            item.EndTime = day.EndTime ?? defaultEnd;
        }
    }

    private static Dictionary<string, LocalBedtimeDayOverride>? CollectBedtimeWeeklyOverrides(
        IEnumerable<LocalDayBedtimeItem> items)
    {
        var weekly = items
            .Where(i => i.OverrideEnabled)
            .ToDictionary(
                i => i.DayKey,
                i => new LocalBedtimeDayOverride
                {
                    Time = i.BedtimeTime.Trim(),
                    WakeTime = i.WakeTime.Trim(),
                },
                StringComparer.OrdinalIgnoreCase);
        return weekly.Count > 0 ? weekly : null;
    }

    private static void SyncBedtimeWeekly(
        ObservableCollection<LocalDayBedtimeItem> items,
        Dictionary<string, LocalBedtimeDayOverride>? weekly,
        string defaultBedtime,
        string defaultWake)
    {
        if (items.Count == 0)
        {
            foreach (var (key, label) in WeekDays)
            {
                items.Add(new LocalDayBedtimeItem
                {
                    DayKey = key,
                    DayLabel = label,
                    BedtimeTime = defaultBedtime,
                    WakeTime = defaultWake,
                });
            }
        }

        foreach (var item in items)
        {
            var hasOverride = weekly?.ContainsKey(item.DayKey) == true;
            item.OverrideEnabled = hasOverride;
            if (!hasOverride)
            {
                item.BedtimeTime = defaultBedtime;
                item.WakeTime = defaultWake;
                continue;
            }

            var day = weekly![item.DayKey];
            item.BedtimeTime = day.Time ?? defaultBedtime;
            item.WakeTime = day.WakeTime ?? defaultWake;
        }
    }

    private static Dictionary<string, int>? CollectWeeklyOverrides(IEnumerable<LocalDayLimitItem> items)
    {
        var weekly = items
            .Where(i => i.OverrideEnabled)
            .ToDictionary(i => i.DayKey, i => Math.Max(0, i.Minutes), StringComparer.OrdinalIgnoreCase);
        return weekly.Count > 0 ? weekly : null;
    }

    private static void SyncWeeklyLimits(
        ObservableCollection<LocalDayLimitItem> items,
        Dictionary<string, int>? weekly,
        int defaultMinutes)
    {
        if (items.Count == 0)
        {
            foreach (var (key, label) in WeekDays)
            {
                items.Add(new LocalDayLimitItem
                {
                    DayKey = key,
                    DayLabel = label,
                    Minutes = defaultMinutes,
                });
            }
        }

        foreach (var item in items)
        {
            var hasOverride = weekly?.ContainsKey(item.DayKey) == true;
            item.OverrideEnabled = hasOverride;
            item.Minutes = hasOverride
                ? weekly![item.DayKey]
                : defaultMinutes;
        }
    }

    private static List<string> ParseStudyDays(string text)
    {
        var days = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant())
            .Where(d => DayScheduleKeys.TryParse(d, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return days.Count > 0 ? days : ["mon", "tue", "wed", "thu", "fri"];
    }

    private void BlockLocalUrl()
    {
        if (!IsLocalMode || string.IsNullOrWhiteSpace(LocalBlockedUrl))
            return;

        if (!TryAuthorizeRestrictionChange("block site"))
            return;

        var host = LocalBlockedUrl.Trim();
        if (_urlBlocking.Block(host))
        {
            if (!_localSettings.Catalog.BlockedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                _localSettings.Catalog.BlockedHosts.Add(host);
                _localSettings.Persist();
            }

            LocalBlockedUrl = string.Empty;
            RefreshRestrictions();
            AddLog($"{UiCopy.MascotName} blocked {host} locally.");
        }
    }

    private void BlockLocalApp()
    {
        if (!IsLocalMode || string.IsNullOrWhiteSpace(LocalBlockedApp))
            return;

        if (!TryAuthorizeRestrictionChange("block app"))
            return;

        var name = LocalBlockedApp.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";

        _sessionState.BlockApp(name, AppBlockCategory.DomManual);
        if (!_localSettings.Catalog.BlockedApps.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _localSettings.Catalog.BlockedApps.Add(name);
            _localSettings.Persist();
        }

        LocalBlockedApp = string.Empty;
        RefreshRestrictions();
        AddLog($"{UiCopy.MascotName} blocked {AppDisplayNames.Resolve(name)} locally.");
    }

    private void TriggerLocalLockScreen()
    {
        if (!IsLocalMode)
            return;

        DomLockRequested();
        AddLog("Screen locked locally.");
    }

    private void LoadLocalImageShieldFields()
    {
        _localSettings.HydrateImageShieldCatalogFromPolicy();
        var catalog = _localSettings.Catalog;
        var perMode = catalog.ImageShieldPerMode ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var perBrowser = catalog.ImageShieldPerBrowser ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        LocalImageShieldEnabled = catalog.ImageShieldEnabled;
        LocalImageShieldTrustedSub = ReadShieldToggle(perMode, AgentModeSlugs.TrustedSub, true);
        LocalImageShieldSub = ReadShieldToggle(perMode, AgentModeSlugs.Sub, true);
        LocalImageShieldRestrictedSub = ReadShieldToggle(perMode, AgentModeSlugs.RestrictedSub, true);
        LocalImageShieldFirefox = ReadShieldToggle(perBrowser, ImageShieldBrowserKeys.Firefox, true);
        LocalImageShieldChrome = ReadShieldToggle(perBrowser, ImageShieldBrowserKeys.Chrome, Config.ExtensionGuardEnforceChromium);
        LocalImageShieldEdge = ReadShieldToggle(perBrowser, ImageShieldBrowserKeys.Edge, false);
        LocalImageShieldBrave = ReadShieldToggle(perBrowser, ImageShieldBrowserKeys.Brave, false);
        LocalImageShieldMinSize = catalog.ImageShieldMinSize ?? 80;
        LocalImageShieldNsfwThreshold = catalog.ImageShieldNsfwThreshold ?? 0.45;
        LocalImageShieldMaxPerSecond = catalog.ImageShieldMaxPerSecond ?? 24;
        RefreshLocalImageShieldStatus();
    }

    private void RefreshLocalImageShieldStatus()
    {
        _localImageShieldFirefoxStatus = BuildBrowserShieldStatus(
            LocalImageShieldFirefox, BrowserKind.Firefox);
        _localImageShieldChromeStatus = BuildBrowserShieldStatus(
            LocalImageShieldChrome, BrowserKind.Chrome);
        _localImageShieldEdgeStatus = BuildBrowserShieldStatus(
            LocalImageShieldEdge, BrowserKind.Edge);
        _localImageShieldBraveStatus = BuildBrowserShieldStatus(
            LocalImageShieldBrave, BrowserKind.Brave);

        OnPropertyChanged(nameof(LocalImageShieldFirefoxStatus));
        OnPropertyChanged(nameof(LocalImageShieldChromeStatus));
        OnPropertyChanged(nameof(LocalImageShieldEdgeStatus));
        OnPropertyChanged(nameof(LocalImageShieldBraveStatus));
        OnPropertyChanged(nameof(ImageShieldStatus));
    }

    private string BuildBrowserShieldStatus(bool domEnabled, BrowserKind kind)
    {
        if (!ImageShieldPolicyService.IsBrowserAgentAvailable(kind))
            return kind == BrowserKind.Firefox ? "Firefox Dev Edition required" : "Work in progress";

        if (!LocalImageShieldEnabled)
            return "Shield off";

        if (!domEnabled)
            return "Disabled";

        if (!_imageShieldPolicy.PoliciesActive)
            return "Waiting to apply";

        if (_imageShieldPolicy.IsEffectivelyEnabled(_agentMode.Slug)
            && _imageShieldPolicy.IsBrowserDomEnabled(kind))
            return "Active";

        return "Configured";
    }

    private static bool ReadShieldToggle(
        IReadOnlyDictionary<string, bool> dict,
        string key,
        bool fallback) =>
        dict.TryGetValue(key, out var enabled) ? enabled : fallback;

    private void CopyImageShieldDiagnostic()
    {
        try
        {
            var report = ExtensionShieldDiagnosticReport.Build(_imageShield);
            Clipboard.SetText(report);
            AddLog(LocalModeCopy.ImageShieldCopyReportDone);
        }
        catch (Exception ex)
        {
            AddLog($"Could not copy shield report: {ex.Message}");
        }
    }

    private void LoadLocalAppTimeLimits(Dictionary<string, int>? limits)
    {
        LocalAppTimeLimits.Clear();
        if (limits is null)
            return;

        foreach (var pair in limits.OrderBy(p => AppDisplayNames.Resolve(p.Key), StringComparer.OrdinalIgnoreCase))
        {
            if (!GamingGameRegistry.TryNormalizeExe(pair.Key, out var exe) || pair.Value <= 0)
                continue;

            LocalAppTimeLimits.Add(new LocalAppTimeLimitItem
            {
                Exe = exe,
                LimitMinutes = Math.Min(pair.Value, 1440),
            });
        }
    }

    private Dictionary<string, int>? CollectLocalAppTimeLimits()
    {
        var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in LocalAppTimeLimits)
        {
            if (!GamingGameRegistry.TryNormalizeExe(item.Exe, out var exe))
                continue;

            if (item.LimitMinutes <= 0)
                continue;

            limits[exe] = Math.Min(item.LimitMinutes, 1440);
        }

        return limits.Count == 0 ? null : limits;
    }

    private void AddLocalAppTimeLimit()
    {
        if (!GamingGameRegistry.TryNormalizeExe(LocalAppLimitExe, out var exe))
        {
            AddLog(LocalModeCopy.AppLimitInvalidExe);
            return;
        }

        var minutes = Math.Clamp(LocalAppLimitMinutes, 1, 1440);
        var existing = LocalAppTimeLimits.FirstOrDefault(item =>
            string.Equals(item.Exe, exe, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.LimitMinutes = minutes;
        }
        else
        {
            LocalAppTimeLimits.Add(new LocalAppTimeLimitItem
            {
                Exe = exe,
                LimitMinutes = minutes,
            });
        }

        LocalAppLimitExe = string.Empty;
        LocalAppLimitMinutes = 30;
    }

    private void RemoveLocalAppTimeLimit(LocalAppTimeLimitItem? item)
    {
        if (item is null)
            return;

        LocalAppTimeLimits.Remove(item);
    }

    private void BrowseLocalCustomGame()
    {
        if (!IsLocalMode)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select game executable",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        var path = dialog.FileName;
        LocalNewGameExe = Path.GetFileName(path);

        // Auto-suggest display name from version info, fallback to filename without extension
        if (string.IsNullOrWhiteSpace(LocalNewGameName))
        {
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                var suggested = !string.IsNullOrWhiteSpace(info.ProductName)
                    ? info.ProductName.Trim()
                    : !string.IsNullOrWhiteSpace(info.FileDescription)
                        ? info.FileDescription.Trim()
                        : Path.GetFileNameWithoutExtension(path);
                LocalNewGameName = suggested;
            }
            catch
            {
                LocalNewGameName = Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    private void LoadLocalCustomGames()
    {
        LocalCustomGames.Clear();
        foreach (var (exe, name) in _gaming.GetExtraGames())
            LocalCustomGames.Add(new LocalCustomGameItem { Exe = exe, DisplayName = name });
    }

    private void LoadLocalWebCategories()
    {
        LocalWebCategories.Clear();
        foreach (var category in WebCategoryCatalog.All)
        {
            var item = new LocalWebCategoryItem(OnWebCategoryToggled)
            {
                Key = category.Key,
                DisplayName = category.DisplayName,
                Description = category.Description,
                Glyph = category.Glyph,
            };
            item.SetBlockedSilent(_webContentFilter.IsEnabled(category.Key));
            LocalWebCategories.Add(item);
        }
    }

    private void OnWebCategoryToggled(LocalWebCategoryItem _)
    {
        if (!IsLocalMode)
            return;

        if (!TryAuthorizeRestrictionChange("web category filter"))
        {
            // Reject the toggle: reload from the authoritative state.
            LoadLocalWebCategories();
            return;
        }

        var enabled = LocalWebCategories.Where(c => c.IsBlocked).Select(c => c.Key).ToList();
        _webContentFilter.SetEnabledCategories(enabled);
        RefreshRestrictions();
        AddLog($"{UiCopy.MascotName} updated web content filtering ({enabled.Count} categories blocked).");
    }

    private void AddLocalCustomGame()
    {
        if (!GamingGameRegistry.TryNormalizeExe(LocalNewGameExe, out var exe))
            return;

        var name = LocalNewGameName.Trim();
        if (string.IsNullOrEmpty(name))
            return;

        _gaming.AddExtraGame(exe, name);
        LoadLocalCustomGames();
        LocalNewGameExe = string.Empty;
        LocalNewGameName = string.Empty;
        AddLog($"Added \"{name}\" ({exe}) to the game list.");
    }

    private void RemoveLocalCustomGame(LocalCustomGameItem? item)
    {
        if (item is null)
            return;

        _gaming.RemoveExtraGame(item.Exe);
        LocalCustomGames.Remove(item);
        AddLog($"Removed \"{item.DisplayName}\" from the game list.");
    }

    private void RemoveLocalExitPin()
    {
        _exitPin.ClearPin();
        LocalExitPin = string.Empty;
        RaiseExitPinState();
        AddLog("Exit PIN removed.");
    }

    private void RaiseExitPinState()
    {
        OnPropertyChanged(nameof(LocalExitPinIsSet));
        OnPropertyChanged(nameof(LocalExitPinStatusText));
        OnPropertyChanged(nameof(ExitPinRequired));
        RemoveLocalExitPinCommand.RaiseCanExecuteChanged();
    }
}

