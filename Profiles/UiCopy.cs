namespace EduGuardAgent.Profiles;

internal static class UiCopy
{
    private static ModeCopySet _tone = ModeCopies.Sub;

    public static void ApplyTone(ModeCopySet tone) => _tone = tone;

    public const string MascotName = "Guardi";

    public static string AppSubtitle => _tone.AppSubtitle;
    public static string StandingLabel(string? subName) =>
        FormatSubName(subName, _tone.StandingLabel, _tone.SubNameFallback);
    public static string SecurityZoneTitle => _tone.SecurityZoneTitle;
    public static string SecurityZoneBody => _tone.SecurityZoneBody;
    public static string InfractionsGood => _tone.InfractionsGood;
    public static string InfractionsWarning => _tone.InfractionsWarning;
    public static string PunishmentDeescalationLabel => _tone.PunishmentDeescalationLabel;
    public static string BedtimeWarningOneHourTitle => _tone.BedtimeWarningOneHourTitle;
    public static string BedtimeWarningOneHourMessage => _tone.BedtimeWarningOneHourMessage;
    public static string BedtimeWarningThirtyTitle => _tone.BedtimeWarningThirtyTitle;
    public static string BedtimeWarningThirtyMessage => _tone.BedtimeWarningThirtyMessage;
    public static string BedtimeWarningFiveTitle => _tone.BedtimeWarningFiveTitle;
    public static string BedtimeWarningFiveMessage => _tone.BedtimeWarningFiveMessage;
    public static string BedtimeLockHeadline(string? subName) =>
        FormatSubName(subName, _tone.BedtimeLockHeadline, _tone.SubNameFallback);
    public static string BedtimeLockBody => _tone.BedtimeLockBody;
    public static string BedtimeLockFooter => _tone.BedtimeLockFooter;
    public static string BedtimeLockCountdownLabel => _tone.BedtimeLockCountdownLabel;
    public static string ScreenTimeLockHeadline(string? subName) =>
        FormatSubName(subName, _tone.ScreenTimeLockHeadline, _tone.SubNameFallback);
    public static string ScreenTimeLockBody => _tone.ScreenTimeLockBody;
    public static string ScreenTimeLockFooter => _tone.ScreenTimeLockFooter;
    public static string PlayTimeLimitReachedTitle => _tone.PlayTimeLimitReachedTitle;
    public static string GameSessionStartTitle => _tone.GameSessionStartTitle;
    public static string StudyTimeBlockedTitle => _tone.StudyTimeBlockedTitle;
    public static string AppBlockedDomTitle => _tone.AppBlockedDomTitle;
    public static string EnrollmentBody => _tone.EnrollmentBody;
    public static string CloseAppWarning => _tone.CloseAppWarning;
    public static string DetailAppsEmpty => _tone.DetailAppsEmpty;
    public static string DetailSitesEmpty => _tone.DetailSitesEmpty;
    public static string ScreenTimeTitle => _tone.ScreenTimeTitle;
    public static string ScreenTimeLimitSuffix => _tone.ScreenTimeLimitSuffix;
    public const string ScreenTimeRemainingFormat = "{0} remaining";
    public const string ScreenTimeLimitReachedToday = "Daily limit reached";
    public static string InfractionsTitle => _tone.InfractionsTitle;
    public static string BedtimeTitle => _tone.BedtimeTitle;
    public static string DomMessageTitle => _tone.DomMessageTitle;
    public static string DomMessageDismissButton => _tone.DomMessageDismissButton;
    public static string RestrictionsTitle => _tone.RestrictionsTitle;
    public static string ActivityTitle => _tone.ActivityTitle;
    public static string ActivityLogTitle => _tone.ActivityLogTitle;
    public static string PlayTimeDetailTotalLabel => _tone.PlayTimeDetailTotalLabel;
    public static string GameListIconGlyph => _tone.GameListIconGlyph;
    public static string VpnShieldTitle => _tone.VpnShieldTitle;
    public static string VpnShieldActive => _tone.VpnShieldActive;
    public static string VpnShieldInactive => _tone.VpnShieldInactive;
    public static string PlayTimeTileTitle => _tone.PlayTimeTileTitle;
    public static string StudyTimeTileTitle => _tone.StudyTimeTileTitle;
    public static string StudyTimeTileActive => _tone.StudyTimeTileActive;
    public static string StudyTimeTileInactive => _tone.StudyTimeTileInactive;
    public static string TaskManagerBlockTitle => _tone.TaskManagerBlockTitle;
    public static string TaskManagerBlockActive => _tone.TaskManagerBlockActive;
    public static string TaskManagerBlockInactive => _tone.TaskManagerBlockInactive;
    public static string ProcessKillerBlockTitle => _tone.ProcessKillerBlockTitle;
    public static string ProcessKillerBlockActive => _tone.ProcessKillerBlockActive;
    public static string ProcessKillerBlockInactive => _tone.ProcessKillerBlockInactive;
    public static string DomWatchingTitle => _tone.DomWatchingTitle;
    public static string DomWatchingDescription => _tone.DomWatchingDescription;
    public static string SafeSearchTitle => _tone.SafeSearchTitle;
    public static string SafeSearchActive => _tone.SafeSearchActive;
    public static string SafeSearchInactive => _tone.SafeSearchInactive;
    public static string SafeSearchNoAdmin => _tone.SafeSearchNoAdmin;
    public static string YouTubeRestrictedModeTitle => _tone.YouTubeRestrictedModeTitle;
    public static string YouTubeRestrictedModeActive => _tone.YouTubeRestrictedModeActive;
    public static string YouTubeRestrictedModeInactive => _tone.YouTubeRestrictedModeInactive;
    public static string YouTubeRestrictedModeNoAdmin => _tone.YouTubeRestrictedModeNoAdmin;
    public static string BlockedAppsTitle => _tone.BlockedAppsTitle;
    public static string BlockedSitesTitle => _tone.BlockedSitesTitle;
    public static string BlockedAppsNone => _tone.BlockedAppsNone;
    public static string BlockedSitesNone => _tone.BlockedSitesNone;
    public static string BlockedAppsTapFormat => _tone.BlockedAppsTapFormat;
    public static string BlockedSitesTapFormat => _tone.BlockedSitesTapFormat;
    public static string IconVpn => _tone.IconVpn;
    public static string IconPlayTime => _tone.IconPlayTime;
    public static string IconStudy => _tone.IconStudy;
    public static string IconTaskManager => _tone.IconTaskManager;
    public const string IconProcessKillers = "🛡️";
    public static string IconDomWatching => _tone.IconDomWatching;
    public static string IconSafeSearch => _tone.IconSafeSearch;
    public static string IconYouTubeRestrictedMode => _tone.IconYouTubeRestrictedMode;
    public static string IconBlockedApps => _tone.IconBlockedApps;
    public static string IconBlockedSites => _tone.IconBlockedSites;
    public static string EnrollmentTitle => _tone.EnrollmentTitle;
    public static string EnrollmentLevelLabel => _tone.EnrollmentLevelLabel;
    public static string EnrollmentCodeLabel => _tone.EnrollmentCodeLabel;
    public static string EnrollmentNameLabel => _tone.EnrollmentNameLabel;
    public static string EnrollmentButton => _tone.EnrollmentButton;
    public static string EnrollmentConnecting => _tone.EnrollmentConnecting;
    public static string EnrollmentFailed => _tone.EnrollmentFailed;
    public static string OnlineProtected => _tone.OnlineProtected;
    public static string OnlineSyncing => _tone.OnlineSyncing;
    public static string StatusSupervisionActive => _tone.StatusSupervisionActive;
    public static string StatusConnectionIssue => _tone.StatusConnectionIssue;
    public static string StatusStarting => _tone.StatusStarting;
    public static string StatusOffline => _tone.StatusOffline;
    public static string StatusSessionEnded => _tone.StatusSessionEnded;
    public static string SupervisionStatusInactive => _tone.SupervisionStatusInactive;
    public static string SupervisionStatusStarting => _tone.SupervisionStatusStarting;
    public static string SupervisionStatusActive => _tone.SupervisionStatusActive;
    public static string SupervisionStatusOffline => _tone.SupervisionStatusOffline;
    public static string SupervisionBadgeInactive => _tone.SupervisionBadgeInactive;
    public static string SupervisionBadgeStarting => _tone.SupervisionBadgeStarting;
    public static string SupervisionBadgeActive => _tone.SupervisionBadgeActive;
    public static string SupervisionBadgeOffline => _tone.SupervisionBadgeOffline;
    public static string TodayRulesTitle => _tone.TodayRulesTitle;
    public static string TodayRulesModeLabel => _tone.TodayRulesModeLabel;
    public static string TodayRulesModeEscalatedFormat => _tone.TodayRulesModeEscalatedFormat;
    public static string TodayRulesScreenLabel => _tone.TodayRulesScreenLabel;
    public static string TodayRulesScreenRemainingFormat => _tone.TodayRulesScreenRemainingFormat;
    public static string TodayRulesScreenExhausted => _tone.TodayRulesScreenExhausted;
    public static string TodayRulesGamingLabel => _tone.TodayRulesGamingLabel;
    public static string TodayRulesGamingRemainingFormat => _tone.TodayRulesGamingRemainingFormat;
    public static string TodayRulesGamingZeroLimit => _tone.TodayRulesGamingZeroLimit;
    public static string TodayRulesYoutubeLabel => _tone.TodayRulesYoutubeLabel;
    public static string TodayRulesYoutubeRemainingFormat => _tone.TodayRulesYoutubeRemainingFormat;
    public static string TodayRulesYoutubeZeroLimit => _tone.TodayRulesYoutubeZeroLimit;
    public static string TodayRulesStudyLabel => _tone.TodayRulesStudyLabel;
    public static string TodayRulesBedtimeLabel => _tone.TodayRulesBedtimeLabel;
    public static string TodayRulesBedtimeOff => _tone.TodayRulesBedtimeOff;
    public static string ConnectionLinkedTitle => _tone.ConnectionLinkedTitle;
    public static string ConnectionLinkedBody => _tone.ConnectionLinkedBody;
    public static string ConnectionOfflineTitle => _tone.ConnectionOfflineTitle;
    public static string ConnectionOfflineBody => _tone.ConnectionOfflineBody;
    public static string ConnectionStartingTitle => _tone.ConnectionStartingTitle;
    public static string ConnectionStartingBody => _tone.ConnectionStartingBody;
    public static string ConnectionLocalTitle => _tone.ConnectionLocalTitle;
    public static string ConnectionLocalBody => _tone.ConnectionLocalBody;
    public static string PlayTimeDetailTitle => _tone.PlayTimeDetailTitle;
    public static string PlayTimeDetailEmpty => _tone.PlayTimeDetailEmpty;
    public static string PlayTimeDetailZeroLimit => _tone.PlayTimeDetailZeroLimit;

    public static string PlayTimeDetailEmptyWithLimitFormat(string remaining, string limit) =>
        string.Format(_tone.PlayTimeDetailEmptyWithLimitFormat, remaining, limit);
    public static string PlayTimeTileZeroLimit => _tone.PlayTimeTileZeroLimit;
    public static string DetailBlockedAppsTitle => _tone.DetailBlockedAppsTitle;
    public static string DetailBlockedSitesTitle => _tone.DetailBlockedSitesTitle;
    public static string DetailAppsEmptySubtitle => _tone.DetailAppsEmptySubtitle;
    public static string DetailSitesEmptySubtitle => _tone.DetailSitesEmptySubtitle;
    public static string CloseAppTitle => _tone.CloseAppTitle;
    public static string CloseAppStayButton => _tone.CloseAppStayButton;
    public static string CloseAppQuitButton => _tone.CloseAppQuitButton;
    public static string DefaultDomMessage => _tone.DefaultDomMessage;

    public static string LevelSubtitle(string modeDisplayName) =>
        string.Format(_tone.LevelSubtitleFormat, modeDisplayName);

    public static string MascotGreeting(string? subName) =>
        FormatSubName(subName, _tone.MascotGreeting, _tone.SubNameFallback);

    private static string FormatSubName(string? subName, string template, string fallback) =>
        string.Format(template, ResolveSubName(subName, fallback));

    private static string ResolveSubName(string? subName, string fallback) =>
        string.IsNullOrWhiteSpace(subName) ? fallback : subName.Trim();

    public static string ScreenTimeLockStatsLabel(string modeDisplayName) =>
        string.Format(_tone.ScreenTimeLockStatsLabelFormat, modeDisplayName);

    public static string PlayTimeLimitReachedMessageFormat(string limitLabel) =>
        string.Format(_tone.PlayTimeLimitReachedMessageFormat, limitLabel);

    public const string PerGameLimitReachedTitle = "This app's play time is up!";
    public const string PerGameLimitReachedMessageFormat = "{0} reached its {1} limit for today — Guardi closed it.";
    public const string AppTimeLimitReachedTitle = "This app's time is up!";
    public const string AppTimeLimitReachedMessageFormat = "{0} reached its {1} daily limit — Guardi closed it.";
    public const string TodayRulesAppRemainingFormat = "{0} left of {1}";
    public static string TodayRulesAppExhaustedFormat(string limit) => $"Limit reached ({limit})";
    public const string AppTimeLimitsCardTitle = "App limits";
    public const string AppTimeLimitsCardSubtitle = "Daily time left for each app";
    public const string AppTimeLimitsEmptyMessage =
        "No apps are limited right now. Add limits in Local settings → App time limits.";
    public const string IconAppTimeLimit = "⏳";
    public const string IconSupervisionMode = "🛡️";
    public const string IconScreenTime = "🖥️";
    public const string IconBedtime = "🌙";

    public static string GameSessionStartMessageFormat(string gameName) =>
        string.Format(_tone.GameSessionStartMessageFormat, gameName);

    public static string StudyTimeBlockedMessageFormat(string gameName) =>
        string.Format(_tone.StudyTimeBlockedMessageFormat, gameName);

    public static string AppBlockedDomMessageFormat(string appName) =>
        string.Format(_tone.AppBlockedDomMessageFormat, appName);

    public static string PlayTimeTileUsageFormat(string used, string limit) =>
        string.Format(_tone.PlayTimeTileUsageFormat, used, limit);

    public static string PlayTimeDetailSubtitleFormat(string used, string limit) =>
        string.Format(_tone.PlayTimeDetailSubtitleFormat, used, limit);

    public static string YoutubeTileUsageFormat(string used, string limit) =>
        string.Format(YoutubeTileUsageTemplate, used, limit);

    public static string YoutubeDetailSubtitleFormat(string used, string limit) =>
        string.Format(YoutubeDetailSubtitleTemplate, used, limit);

    public static string YoutubeLimitReachedMessageFormat(string limitLabel) =>
        string.Format(YoutubeLimitReachedMessageTemplate, limitLabel);

    public static string ActivityAppsFormat(int count) =>
        string.Format(_tone.ActivityAppsFormat, count);

    public static string DetailAppsCountFormat(int count) =>
        string.Format(_tone.DetailAppsCountFormat, count);

    public static string DetailSitesCountFormat(int count) =>
        string.Format(_tone.DetailSitesCountFormat, count);

    public static string SystemToolsLockActiveFormat(string tools) =>
        string.Format(SystemToolsLockActiveTemplate, tools);

    public const string SystemToolsLockTitle = "System tools";
    public const string SystemToolsLockInactive = "System tools are allowed";
    private const string SystemToolsLockActiveTemplate = "Locked: {0}";
    public const string SystemToolRegistry = "Registry Editor";
    public const string SystemToolCommandPrompt = "Command Prompt";
    public const string SystemToolPowerShell = "PowerShell";
    public const string SystemToolSystemConfig = "System Configuration";
    public const string SystemToolControlPanel = "Control Panel";
    public const string SystemToolProcessTools = "Process tools";
    public const string SystemToolProcessKillers = "Auto process killers";
    public const string IconSystemTools = "🛡";

    public const string CloseProtectionTitle = "Close protection";
    public const string CloseProtectionActive =
        "EduGuard cannot be closed from Task Manager";
    public const string IconCloseProtection = "🔒";

    public const string DomLockDismissLabel = "Unlock";
    public const string DomLockDismissHint = "Your Dom's PIN is required to unlock";
    public const string BedtimeLockDismissLabel = "Unlock";
    public const string BedtimeLockDismissHint = "Your Dom's PIN is required to unlock";
    public const string ScreenTimeLockDismissLabel = "Leave lock screen";
    public const string ScreenTimeLockDismissHint = "Your Dom's PIN is required to unlock";

    public const string DomLockHeadline = "Your computer has been locked by your Dom";
    public const string DomLockBody =
        "Your Dom decided this computer needs a time-out. Sit still and wait — Guardi won't let you sneak past this screen.";
    public const string DomLockFooter =
        "Only your Dom can unlock this screen from their dashboard.";

    public const string AppBlockedDomImmediateTitle = "Your Dom closed that app!";
    public const string AppBlockedDomImmediateMessage =
        "Whoa there! Your Dom just shut down {0}. When your Dom says stop, Guardi listens right away — no sneaking back!";
    public const string AppBlockedVpnTitle = "No sneaky tunnels!";
    public const string AppBlockedVpnMessage =
        "Nice try! {0} is a VPN, and VPNs are a big no-no while Guardi is watching over you. Your Dom wants you safe and visible — not hidden behind a secret tunnel.";
    public const string AppBlockedDefaultTitle = "Guardi closed that app";
    public const string AppBlockedDefaultMessage =
        "{0} isn't allowed right now on this computer. Guardi is following your safety rules — ask your Dom if you think something's wrong.";
    public const string SecurityToolBlockedTitle = "No sneaking past Guardi!";
    public const string SecurityToolBlockedMessage =
        "Nice try! {0} isn't allowed while Guardi is watching — your Dom keeps those tools locked down so you can't bypass the rules.";

    public const string ExitPinTitle = "Dom PIN";
    public const string ExitPinPrompt = "Enter your Dom's PIN to continue.";
    public const string ExitPinUnlinkPrompt = "Enter your Dom's PIN to unlink this device.";
    public const string ExitPinConfirm = "Confirm";
    public const string ExitPinCancel = "Cancel";
    public const string ExitPinWrong = "Incorrect PIN.";
    public const string ExitPinLockout = "Too many attempts. Try again in {0}s.";

    public const string SubNamePromptTitle = "Let's be friends!";
    public const string SubNamePromptBody = "Tell me your name so I know how to call you!";
    public const string SubNamePromptConfirm = "Let's go!";
    public const string SubNamePromptInvalid = "Oopsie, I need at least one letter to work with!";

    // First-run setup wizard — step 1: local vs online.
    public const string WelcomeModeTitle = "Hiya, I'm Guardi!";
    public const string WelcomeModeSubtitle = "So... where do you want me to keep watch from?";
    public const string WelcomeModeLocalTitle = "Right here with you!";
    public const string WelcomeModeLocalBody = "I'll live right on this computer — no account, no fuss!";
    public const string WelcomeModeLocalButton = "Set up locally";
    public const string WelcomeModeOnlineTitle = "From a web dashboard";
    public const string WelcomeModeOnlineBody = "Coming soon — your Dom will be able to manage me from anywhere!";
    public const string WelcomeModeOnlineBadge = "Coming soon";

    // First-run setup wizard — step 2: name (reuses SubNamePrompt copy above).

    // First-run setup wizard — step 3: PIN (local only).
    public const string WelcomePinTitle = "Let's keep a secret code";
    public const string WelcomePinBody =
        "Pick a secret PIN so only your Dom can change my settings. Don't worry, you (or they) " +
        "can change it again later!";
    public const string WelcomePinConfirm = "Lock it in!";
    public const string WelcomePinSkip = "Skip for now";
    public const string WelcomePinInvalid = "Needs to be 6 to 8 numbers, sweetie!";

    // Post-onboarding welcome tour — mandatory, multi-page, each mode page themed like that mode.
    public const string TourWhyTitle = "So, why am I here?";
    public const string TourWhyBody =
        "I'm here to help keep things safe and balanced while you're online! I can blur risky " +
        "pictures before they even load, keep an eye on your screen and game time, and gently " +
        "steer you away from sites your Dom would rather you skip. I'm your cozy helper, not " +
        "the bad guy — I just want to help you build good habits!";
    public const string TourNext = "Tell me more!";
    public const string TourBack = "Back";

    public const string TourTrustedSubTitle = "Meet Schooly!";
    public const string TourTrustedSubKicker = "Trusted Sub";
    public const string TourTrustedSubBody =
        "Schooly comes out when your Dom trusts you the most! It's my chillest look — I mostly " +
        "just watch and give friendly nudges instead of hard blocks. Keep that trust up and " +
        "Schooly sticks around.";

    public const string TourSubTitle = "Meet Guardy!";
    public const string TourSubKicker = "Sub";
    public const string TourSubBody =
        "Guardy is my everyday, standard look. I'll actually block the risky stuff and keep a " +
        "closer eye on your screen time here — still cozy, just a bit more hands-on.";

    public const string TourRestrictedSubTitle = "Meet Locky!";
    public const string TourRestrictedSubKicker = "Restricted Sub";
    public const string TourRestrictedSubBody =
        "Locky shows up when extra focus or safety is needed. It's my strictest look — tighter " +
        "limits and locked-down tools. Don't worry, it's not forever — good habits can earn an " +
        "easier mode back.";

    public const string TourTrustTitle = "I keep a trust meter for you!";
    public const string TourTrustBody =
        "It starts full! If you push limits — like ignoring time limits or trying to dodge me — " +
        "it dips a little. Let it run too low and I'll switch to a stricter mode on my own. But " +
        "here's the good part: behaving well fills it back up, and enough good behavior can even " +
        "earn your way back to an easier mode!";
    public const string TourTrustFullLabel = "Full trust";
    public const string TourTrustLowLabel = "Low trust";
    public const string TourFinish = "Got it, let's go!";

    public const string RestrictionActive = "ON";
    public const string RestrictionInactive = "Off";

    public const string GamingHudTitle = "Play time left";

    public const string YoutubeHudTitle = "YouTube time left";
    public const string YoutubeTileTitle = "YouTube time";
    public const string YoutubeTileUsageTemplate = "{0} of {1} used today — tap for details";
    public const string YoutubeDetailTitle = "Your YouTube time today";
    public const string YoutubeDetailSubtitleTemplate = "{0} of {1} used today";
    public const string YoutubeDetailTotalLabel = "Total YouTube time today";
    public const string YoutubeDetailEmpty = "No YouTube watched today. Time counts when a YouTube tab is open and visible.";
    public const string YoutubeLimitReachedTitle = "YouTube time's up!";
    public const string YoutubeLimitReachedMessageTemplate =
        "You've used your {0} of YouTube time for today. Guardi replaced the page — ask your Dom if you need more.";
    public const string IconYoutube = "▶️";

    public const string CloseAppPasswordHint = "Your Dom's password will be required here later";
    public const string TrayTooltip = "Guardi — click to open";
    public const string TrayMenuTitle = "Guardi";
    public const string TrayMenuSubtitle = "Still protecting this PC in the background";
    public const string TrayOpenMenuItem = "Open Guardi";
    public const string TrayQuitMenuItem = "Quit Guardi…";
    public const string TrayHideBalloonTitle = "Guardi is still running";
    public const string TrayHideBalloonMessage =
        "Guardi keeps protecting this PC in the background. Open it from the hidden icons near the clock, or click the Guardi widget on your desktop.";
    public const string HideWindowTooltip = "Hide Guardi (keeps running)";
    public const string TrayQuitHint =
        "Quitting turns off all Guardi protections on this PC. To hide the window instead, use the close button on the dashboard.";
    public const string ResetEnrollmentButton = "Unlink this device";

    // Punishment / auto-escalation
    public const string PunishmentEscalationTitle = "Stricter mode turned on";
    public const string PunishmentEscalationMessage =
        "Too many rule-breaks, so Guardi turned up the protection to {0}. Behave for a while and it will ease back down on its own.";
    public const string PunishmentLevelChangedLog = "Enforcement level is now {0}.";
    public const string PunishmentSettingsUpdatedLog = "Guardi updated the punishment rules.";
    public const string PunishmentResetLog = "Your Dom cleared the punishment and reset the infraction count.";
    public const string InfractionLogPrefix = "Infraction";

    public const string InfractionLabelVpn = "VPN or tunnel apps";
    public const string InfractionHintVpn = "Launching a VPN, proxy, or tunnel app.";
    public const string InfractionLabelBlockedApp = "Repeating blocked apps";
    public const string InfractionHintBlockedApp = "Opening the same blocked app again after Guardi closed it.";
    public const string InfractionLabelBypass = "System bypass tools";
    public const string InfractionHintBypass = "Task Manager, Registry Editor, CMD, PowerShell, etc.";
    public const string InfractionLabelLimit = "Ignoring time limits";
    public const string InfractionHintLimit = "Continuing after screen time, play time, or YouTube limit.";
    public const string InfractionLabelStudy = "Study-time distractions";
    public const string InfractionHintStudy = "Games, social apps, or other distractions during study hours.";
    public const string InfractionLabelBlockedSearch = "Blocked searches";
    public const string InfractionHintBlockedSearch = "Adult or blocked search terms in a protected browser.";
    public const string InfractionLabelGeneric = "Rule break";

    public const string InfractionVpnDetail = "Tried to launch a VPN ({0}).";
    public const string InfractionBlockedAppDetail = "Kept trying to open a blocked app ({0}).";
    public const string InfractionBypassToolDetail = "Tried to open a blocked system tool ({0}).";
    public const string InfractionBypassPinDetail = "Failed the Dom PIN on a protected action ({0}).";
    public const string InfractionStudyDetail = "Tried to use {0} during study time.";

    public const string StudyStartedTitle = "Study time started";
    public const string StudyStartedMessage = "Focus until {0}. Guardi is blocking distractions.";
    public const string StudyEndedTitle = "Study time ended";
    public const string StudyEndedMessage = "Study window is over — normal rules apply again.";

    public static string InfractionBlockedSearchDetailFormat => _tone.InfractionBlockedSearchDetailFormat;
    public const string InfractionScreenTimeDetail = "Reached the daily screen-time limit and kept going.";
    public const string InfractionGamingDetail = "Reached the daily game-time limit and kept going.";
    public const string InfractionYoutubeDetail = "Reached the daily YouTube limit and kept going.";

    // Soft enforcement (Trusted Sub): nothing is closed/locked — Guardi just reminds and
    // lets trust slip. (Phase C will give these per-mode / per-zone variety.)
    public const string ScreenTimeSoftReminderLog =
        "Screen time's up — Guardi's trusting you to wrap up. Trust dips a little if you keep going.";
    public const string SoftLimitReminderFormat =
        "{0} reached its limit — Guardi's leaving it open and trusting you. Trust slips if you keep going.";

    // Shown in the gaming/YouTube HUD countdown once the limit is exhausted, instead of a
    // frozen "00:00" timer. In Trusted Sub the app keeps running (soft enforcement), so the
    // HUD stays up — this makes it read as spent rather than a stuck clock.
    public const string HudTimesUpLabel = "Time's up!";

    // Fullscreen soft warning (Trusted Sub only) when the global screen-time limit is hit —
    // not a lock, but the Sub has to actively click through it rather than it being silent.
    public const string SoftLimitWarningTitle = "Time's up for today!";
    public const string SoftLimitWarningBody =
        "You've reached your screen time limit for today. Schooly trusts you to wrap things up " +
        "now — nothing's locked since you're in Trusted Sub mode, but pushing through still costs " +
        "you a little trust.";
    public const string SoftLimitWarningFooter =
        "Wrapping up now keeps your trust meter happy and your Dom proud.";
    public const string SoftLimitWarningContinueButton = "Continue anyway (not recommended)";

    // Gaming soft limit overlay (Trusted Sub — game keeps running, user must ack to continue)
    public const string GamingSoftLimitTitle = "Play time's up for today!";
    public static string GamingSoftLimitMessage(string limitLabel) =>
        $"You've used all your game time ({limitLabel}). Guardi's leaving it open since you're in Trusted Sub mode — but keep playing and your trust slips a little.";
    public const string GamingSoftLimitHint = "Stopping now keeps your trust meter happy.";
    public const string GamingSoftLimitStopButton = "Stop playing";
    public const string GamingSoftLimitContinueButton = "Keep playing anyway";

    // Periodic nag toasts when a soft limit is actively being ignored (every 3 min).
    public static string NagTitle => MascotName + " noticed…";
    private static readonly string[] LimitIgnoredNags =
    [
        "You were supposed to stop. Every extra minute costs you a little more trust.",
        "Still going? {0} has been off-limits for a while now. Guardi's keeping count.",
        "Your Dom set that limit for a reason. Each minute you ignore it chips away at your trust.",
        "Guardi sees you ignoring the {0} limit. This will show on your trust report.",
        "Still at it? That limit wasn't a suggestion. Trust dropping…",
        "The longer you ignore this, the harder it'll be to earn trust back.",
        "Guardi's watching. Every minute past the limit is noted.",
    ];
    private static int _nagIndex;
    public static string NextLimitIgnoredNag(string zone) =>
        string.Format(
            LimitIgnoredNags[Math.Abs(System.Threading.Interlocked.Increment(ref _nagIndex)) % LimitIgnoredNags.Length],
            zone);

    public const string SoftLimitWarningYoutubeTitle = "YouTube time's up for today!";
    public const string SoftLimitWarningYoutubeBody =
        "You've reached your daily YouTube limit. Schooly trusts you to close it up now — " +
        "nothing's locked since you're in Trusted Sub mode, but pushing through still costs " +
        "you a little trust.";

    public const string SoftLimitWarningBedtimeTitle = "It's past your bedtime!";
    public const string SoftLimitWarningBedtimeBody =
        "Your bedtime has arrived. Guardi trusts you to save your work and get to bed — " +
        "nothing's locked since you're in Trusted Sub mode, but every 10 minutes you stay up " +
        "costs you a little more trust.";

    public const string DisciplineEscalatedFormat = "Base: {0} → now {1}";
    public const string DisciplineProgressFormat = "{0}/{1} toward {2}";
    public const string DisciplineMaxLevelText = "At maximum supervision level";
    public const string DisciplineDisabledHint = "Trust tracking is off.";

    // Trust gauge — warm, in Guardi's voice. {0} = current trust (0-100).
    public const string TrustConfidenceFormat = "Trust {0}/100 — Guardi's proud of you. 💙";
    public const string TrustCautionFormat = "Trust {0}/100 — easy now, let's keep it cosy.";
    public const string TrustHardeningFormat = "Trust {0}/100 — getting shaky. One more slip turns things stricter.";
    public const string TrustEscalationFormat = "Trust {0}/100 — Guardi had to tighten up.";

    public static string TrustZoneText(int trust, EduGuardAgent.Models.TrustZone zone) => zone switch
    {
        EduGuardAgent.Models.TrustZone.Confidence => string.Format(TrustConfidenceFormat, trust),
        EduGuardAgent.Models.TrustZone.Caution => string.Format(TrustCautionFormat, trust),
        EduGuardAgent.Models.TrustZone.Hardening => string.Format(TrustHardeningFormat, trust),
        _ => string.Format(TrustEscalationFormat, trust),
    };

    public static string UrlBlockingStatus(int count, bool serverRunning, string? hostsError, bool orphan) =>
        hostsError is not null
            ? $"Website shield issue: {hostsError}"
            : orphan
                ? "Website shield out of sync — restart EduGuard to fix"
                : count == 0
                    ? "No blocked websites configured"
                    : !serverRunning
                        ? $"{count} blocked site(s) — shield page offline"
                        : $"{count} blocked site(s) active";

    public static string ImageShieldStatus(
        bool admin,
        bool storeConfigured,
        bool policiesActive,
        string? error) =>
        !admin
            ? "Image shield: needs an elevated terminal (CMD admin + dotnet run)"
            : !storeConfigured
                ? error ?? "Image shield: run npm.cmd run build:firefox in extension/"
                : policiesActive
                    ? Config.ExtensionGuardEnforceChromium
                        ? "Image shield: active — Chrome Web Store + Firefox"
                        : "Image shield: active — Firefox Developer Edition (local)"
                    : error ?? "Image shield: waiting to apply browser policies";
}
