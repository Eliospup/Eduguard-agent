namespace EduGuardAgent.Profiles;

using EduGuardAgent.Models;

internal sealed class ModeCopySet
{
    public required string AppSubtitle { get; init; }
    public required string LevelSubtitleFormat { get; init; }
    public required string StandingLabel { get; init; }
    public required string SecurityZoneTitle { get; init; }
    public required string SecurityZoneBody { get; init; }
    public required string MascotGreeting { get; init; }
    public required string SubNameFallback { get; init; }
    public required string InfractionsGood { get; init; }
    public required string InfractionsWarning { get; init; }
    public required string PunishmentDeescalationLabel { get; init; }
    public required string BedtimeWarningOneHourTitle { get; init; }
    public required string BedtimeWarningOneHourMessage { get; init; }
    public required string BedtimeWarningThirtyTitle { get; init; }
    public required string BedtimeWarningThirtyMessage { get; init; }
    public required string BedtimeWarningFiveTitle { get; init; }
    public required string BedtimeWarningFiveMessage { get; init; }
    public required string BedtimeLockHeadline { get; init; }
    public required string BedtimeLockBody { get; init; }
    public required string BedtimeLockFooter { get; init; }
    public required string BedtimeLockCountdownLabel { get; init; }
    public required string ScreenTimeLockHeadline { get; init; }
    public required string ScreenTimeLockBody { get; init; }
    public required string ScreenTimeLockStatsLabelFormat { get; init; }
    public required string ScreenTimeLockFooter { get; init; }
    public required string PlayTimeLimitReachedTitle { get; init; }
    public required string PlayTimeLimitReachedMessageFormat { get; init; }
    public required string GameSessionStartTitle { get; init; }
    public required string GameSessionStartMessageFormat { get; init; }
    public required string StudyTimeBlockedTitle { get; init; }
    public required string StudyTimeBlockedMessageFormat { get; init; }
    public required string AppBlockedDomTitle { get; init; }
    public required string AppBlockedDomMessageFormat { get; init; }
    public required string EnrollmentBody { get; init; }
    public required string CloseAppWarning { get; init; }
    public required string DetailAppsEmpty { get; init; }
    public required string DetailSitesEmpty { get; init; }

    public required string ScreenTimeTitle { get; init; }
    public required string ScreenTimeLimitSuffix { get; init; }
    public required string InfractionsTitle { get; init; }
    public required string BedtimeTitle { get; init; }
    public required string DomMessageTitle { get; init; }
    public required string DomMessageDismissButton { get; init; }
    public required string RestrictionsTitle { get; init; }
    public required string ActivityTitle { get; init; }
    public required string ActivityLogTitle { get; init; }
    public required string PlayTimeDetailTotalLabel { get; init; }
    public required string GameListIconGlyph { get; init; }
    public required string VpnShieldTitle { get; init; }
    public required string VpnShieldActive { get; init; }
    public required string VpnShieldInactive { get; init; }
    public required string PlayTimeTileTitle { get; init; }
    public required string PlayTimeTileUsageFormat { get; init; }
    public required string PlayTimeTileZeroLimit { get; init; }
    public required string StudyTimeTileTitle { get; init; }
    public required string StudyTimeTileActive { get; init; }
    public required string StudyTimeTileInactive { get; init; }
    public required string TaskManagerBlockTitle { get; init; }
    public required string TaskManagerBlockActive { get; init; }
    public required string TaskManagerBlockInactive { get; init; }
    public required string ProcessKillerBlockTitle { get; init; }
    public required string ProcessKillerBlockActive { get; init; }
    public required string ProcessKillerBlockInactive { get; init; }
    public required string DomWatchingTitle { get; init; }
    public required string DomWatchingDescription { get; init; }
    public required string SafeSearchTitle { get; init; }
    public required string SafeSearchActive { get; init; }
    public required string SafeSearchInactive { get; init; }
    public required string SafeSearchNoAdmin { get; init; }
    public required string YouTubeRestrictedModeTitle { get; init; }
    public required string YouTubeRestrictedModeActive { get; init; }
    public required string YouTubeRestrictedModeInactive { get; init; }
    public required string YouTubeRestrictedModeNoAdmin { get; init; }
    public required string BlockedAppsTitle { get; init; }
    public required string BlockedSitesTitle { get; init; }
    public required string BlockedAppsNone { get; init; }
    public required string BlockedSitesNone { get; init; }
    public required string BlockedAppsTapFormat { get; init; }
    public required string BlockedSitesTapFormat { get; init; }
    public required string IconVpn { get; init; }
    public required string IconPlayTime { get; init; }
    public required string IconStudy { get; init; }
    public required string IconTaskManager { get; init; }
    public required string IconDomWatching { get; init; }
    public required string IconSafeSearch { get; init; }
    public required string IconYouTubeRestrictedMode { get; init; }
    public required string IconBlockedApps { get; init; }
    public required string IconBlockedSites { get; init; }
    public required string EnrollmentTitle { get; init; }
    public required string EnrollmentLevelLabel { get; init; }
    public required string EnrollmentCodeLabel { get; init; }
    public required string EnrollmentNameLabel { get; init; }
    public required string EnrollmentButton { get; init; }
    public required string EnrollmentConnecting { get; init; }
    public required string EnrollmentFailed { get; init; }
    public required string OnlineProtected { get; init; }
    public required string OnlineSyncing { get; init; }
    public required string StatusSupervisionActive { get; init; }
    public required string StatusConnectionIssue { get; init; }
    public required string StatusStarting { get; init; }
    public required string StatusOffline { get; init; }
    public required string StatusSessionEnded { get; init; }
    public required string SupervisionStatusInactive { get; init; }
    public required string SupervisionStatusStarting { get; init; }
    public required string SupervisionStatusActive { get; init; }
    public required string SupervisionStatusOffline { get; init; }
    public required string SupervisionBadgeInactive { get; init; }
    public required string SupervisionBadgeStarting { get; init; }
    public required string SupervisionBadgeActive { get; init; }
    public required string SupervisionBadgeOffline { get; init; }
    public required string TodayRulesTitle { get; init; }
    public required string TodayRulesModeLabel { get; init; }
    public required string TodayRulesModeEscalatedFormat { get; init; }
    public required string TodayRulesScreenLabel { get; init; }
    public required string TodayRulesScreenRemainingFormat { get; init; }
    public required string TodayRulesScreenExhausted { get; init; }
    public required string TodayRulesGamingLabel { get; init; }
    public required string TodayRulesGamingRemainingFormat { get; init; }
    public required string TodayRulesGamingZeroLimit { get; init; }
    public required string TodayRulesYoutubeLabel { get; init; }
    public required string TodayRulesYoutubeRemainingFormat { get; init; }
    public required string TodayRulesYoutubeZeroLimit { get; init; }
    public required string TodayRulesStudyLabel { get; init; }
    public required string TodayRulesBedtimeLabel { get; init; }
    public required string TodayRulesBedtimeOff { get; init; }
    public required string ConnectionLinkedTitle { get; init; }
    public required string ConnectionLinkedBody { get; init; }
    public required string ConnectionOfflineTitle { get; init; }
    public required string ConnectionOfflineBody { get; init; }
    public required string ConnectionStartingTitle { get; init; }
    public required string ConnectionStartingBody { get; init; }
    public required string ConnectionLocalTitle { get; init; }
    public required string ConnectionLocalBody { get; init; }
    public required string PlayTimeDetailTitle { get; init; }
    public required string PlayTimeDetailSubtitleFormat { get; init; }
    public required string PlayTimeDetailEmpty { get; init; }
    public required string PlayTimeDetailEmptyWithLimitFormat { get; init; }
    public required string PlayTimeDetailZeroLimit { get; init; }
    public required string DetailBlockedAppsTitle { get; init; }
    public required string DetailBlockedSitesTitle { get; init; }
    public required string DetailAppsEmptySubtitle { get; init; }
    public required string DetailSitesEmptySubtitle { get; init; }
    public required string DetailAppsCountFormat { get; init; }
    public required string DetailSitesCountFormat { get; init; }
    public required string CloseAppTitle { get; init; }
    public required string CloseAppStayButton { get; init; }
    public required string CloseAppQuitButton { get; init; }
    public required string ActivityAppsFormat { get; init; }
    public required string DefaultDomMessage { get; init; }
    public required string InfractionBlockedSearchDetailFormat { get; init; }
}

internal static class ModeCopies
{
    public static ModeCopySet Sub { get; } = new()
    {
        AppSubtitle = "Safe & supervised computer",
        LevelSubtitleFormat = "{0} • No infractions yet!",
        StandingLabel = "Hi {0} — your Dom is protecting this computer right now",
        SecurityZoneTitle = "You're in the Safety Zone!",
        SecurityZoneBody =
            "Guardi is watching over you. Stay on approved sites and follow your Dom's safety rules.",
        MascotGreeting = "Hi {0}! I'm Guardi — I'll keep you safe today.",
        SubNameFallback = "sweetie",
        InfractionsGood = "Good little Sub — no infractions!",
        InfractionsWarning = "Breaking safety rules can make your Dom tighten protections.",
        PunishmentDeescalationLabel = "Easier rules in",
        BedtimeWarningOneHourTitle = "Bedtime is coming, little one",
        BedtimeWarningOneHourMessage =
            "Just one hour until sleepy time! Guardi is turning on a gentle warm screen filter to help you wind down — start wrapping up what you're doing.",
        BedtimeWarningThirtyTitle = "Half an hour till beddy-bye",
        BedtimeWarningThirtyMessage =
            "Only 30 minutes left before bedtime. Your Dom wants you winding down, not sneaking in one more round.",
        BedtimeWarningFiveTitle = "Five more minutes!",
        BedtimeWarningFiveMessage =
            "Five tiny minutes, then lights out! Say goodnight to your apps — Guardi is about to turn them in for the night.",
        BedtimeLockHeadline = "Sweet dreams, {0}",
        BedtimeLockBody =
            "It's bedtime! Your Dom said this computer needs to sleep now. Guardi tucked the screen in tight — no more peeking tonight.",
        BedtimeLockFooter =
            "This screen stays cozy and locked until your Dom's wake-up time. Only your Dom can change sleepy time.",
        BedtimeLockCountdownLabel = "Guardi unlocks your computer in",
        ScreenTimeLockHeadline = "Time's up, {0}!",
        ScreenTimeLockBody =
            "You've used all your screen allowance for today. Ask your Dom nicely for more time, or come back tomorrow when Guardi resets your clock.",
        ScreenTimeLockStatsLabelFormat = "{0} daily safety limit",
        ScreenTimeLockFooter =
            "This computer stays protected until your Dom says you can have more screen time.",
        PlayTimeLimitReachedTitle = "Play time's up!",
        PlayTimeLimitReachedMessageFormat =
            "You've used all your game time for today ({0}). Guardi closed your games — ask your Dom nicely for more, or come back tomorrow.",
        GameSessionStartTitle = "Have fun, sweetie!",
        GameSessionStartMessageFormat =
            "Guardi wishes you a cozy {0} session — play nice and mind your time!",
        StudyTimeBlockedTitle = "Study time — no games!",
        StudyTimeBlockedMessageFormat =
            "It's study time right now, little one! {0} isn't allowed until your Dom's study window ends. Guardi tucked it away so you can focus.",
        AppBlockedDomTitle = "Nope! Your Dom said no",
        AppBlockedDomMessageFormat =
            "Oopsie, little one! {0} isn't allowed on this protected computer. Your Dom put it on the naughty list — so Guardi had to tuck it away for you.",
        EnrollmentBody =
            "Your Dom made a special code. Enter it so Guardi can start protecting you.",
        CloseAppWarning =
            "This computer is under your Dom's protection. Are you sure you want to turn Guardi off?",
        DetailAppsEmpty = "Your Dom hasn't blocked any apps yet. Guardi is still keeping watch!",
        DetailSitesEmpty = "Your Dom hasn't blocked any websites yet. Guardi is still keeping watch!",
        ScreenTimeTitle = "Today's screen allowance",
        ScreenTimeLimitSuffix = "daily allowance",
        InfractionsTitle = "Safety infractions",
        BedtimeTitle = "Sleepy time",
        DomMessageTitle = "Important message from your Dom",
        DomMessageDismissButton = "Got it!",
        RestrictionsTitle = "Your safety rules",
        ActivityTitle = "What you're looking at",
        ActivityLogTitle = "Safety log",
        PlayTimeDetailTotalLabel = "Total game time today",
        GameListIconGlyph = "🎮",
        VpnShieldTitle = "No sneaky tunnels",
        VpnShieldActive = "VPN tunnels are blocked — no sneaking around Guardi",
        VpnShieldInactive = "VPN shield is off — ask your Dom to relink",
        PlayTimeTileTitle = "Play time limit",
        PlayTimeTileUsageFormat = "{0} of {1} games today — tap for details",
        PlayTimeTileZeroLimit = "0 min daily limit — play time blocked",
        StudyTimeTileTitle = "Study time",
        StudyTimeTileActive = "Study mode active — games blocked",
        StudyTimeTileInactive = "Games allowed outside study hours",
        TaskManagerBlockTitle = "Task Manager blocked",
        TaskManagerBlockActive = "Task Manager is blocked in this mode",
        TaskManagerBlockInactive = "Task Manager is allowed",
        ProcessKillerBlockTitle = "Process killers blocked",
        ProcessKillerBlockActive = "Auto process-killer tools (pk.exe, etc.) are shut down",
        ProcessKillerBlockInactive = "Auto process-killer tools are allowed",
        DomWatchingTitle = "Dom is watching",
        DomWatchingDescription = "Your Dom can see what you do here",
        SafeSearchTitle = "SafeSearch shield",
        SafeSearchActive = "Grown-up searches are filtered in your browsers",
        SafeSearchInactive = "SafeSearch could not be locked",
        SafeSearchNoAdmin = "SafeSearch needs administrator rights to protect you",
        YouTubeRestrictedModeTitle = "YouTube restricted mode",
        YouTubeRestrictedModeActive = "Strict YouTube filtering is locked in Chrome, Edge and Brave",
        YouTubeRestrictedModeInactive = "YouTube restricted mode is off",
        YouTubeRestrictedModeNoAdmin = "YouTube restricted mode needs administrator rights",
        BlockedAppsTitle = "Blocked apps",
        BlockedSitesTitle = "Blocked websites",
        BlockedAppsNone = "No apps blocked — you're clear!",
        BlockedSitesNone = "No websites blocked — you're clear!",
        BlockedAppsTapFormat = "{0} app(s) blocked — tap to see",
        BlockedSitesTapFormat = "{0} site(s) blocked — tap to see",
        IconVpn = "🛡️",
        IconPlayTime = "🎮",
        IconStudy = "📚",
        IconTaskManager = "🧰",
        IconDomWatching = "👁️",
        IconSafeSearch = "🔍",
        IconYouTubeRestrictedMode = "▶️",
        IconBlockedApps = "🚫",
        IconBlockedSites = "🌐",
        EnrollmentTitle = "Link this computer",
        EnrollmentLevelLabel = "Starting supervision mode:",
        EnrollmentCodeLabel = "Secret link code",
        EnrollmentNameLabel = "Computer nickname",
        EnrollmentButton = "Start my protected session",
        EnrollmentConnecting = "Guardi is connecting you to your Dom…",
        EnrollmentFailed = "That code didn't work. Ask your Dom for a fresh one.",
        OnlineProtected = "Protected & linked",
        OnlineSyncing = "Reconnecting to safety net…",
        StatusSupervisionActive = "Safety mode active",
        StatusConnectionIssue = "Safety link interrupted",
        StatusStarting = "Starting your protection…",
        StatusOffline = "Not protected yet",
        StatusSessionEnded = "Protection paused — ask your Dom to link again",
        SupervisionStatusInactive = "Supervision inactive",
        SupervisionStatusStarting = "Supervision starting…",
        SupervisionStatusActive = "Supervision active",
        SupervisionStatusOffline = "Rules active — dashboard offline",
        SupervisionBadgeInactive = "OFF",
        SupervisionBadgeStarting = "…",
        SupervisionBadgeActive = "ON",
        SupervisionBadgeOffline = "LOC",
        TodayRulesTitle = "Today's rules",
        TodayRulesModeLabel = "Supervision mode",
        TodayRulesModeEscalatedFormat = "{0} (tightened from {1})",
        TodayRulesScreenLabel = "Screen time",
        TodayRulesScreenRemainingFormat = "{0} left of {1}",
        TodayRulesScreenExhausted = "Daily limit reached",
        TodayRulesGamingLabel = "Gaming",
        TodayRulesGamingRemainingFormat = "{0} left of {1}",
        TodayRulesGamingZeroLimit = "Blocked all day (0 min)",
        TodayRulesYoutubeLabel = "YouTube",
        TodayRulesYoutubeRemainingFormat = "{0} left of {1}",
        TodayRulesYoutubeZeroLimit = "Blocked all day (0 min)",
        TodayRulesStudyLabel = "Study time",
        TodayRulesBedtimeLabel = "Bedtime",
        TodayRulesBedtimeOff = "No bedtime tonight",
        ConnectionLinkedTitle = "Linked to your Dom",
        ConnectionLinkedBody = "Rules sync live from the dashboard — you're covered.",
        ConnectionOfflineTitle = "Dashboard offline",
        ConnectionOfflineBody = "Your Dom's rules still apply on this PC until the link comes back.",
        ConnectionStartingTitle = "Connecting to your Dom…",
        ConnectionStartingBody = "Guardi is opening the safety link — hang on a moment.",
        ConnectionLocalTitle = "Local supervision",
        ConnectionLocalBody = "Web dashboard paused — only rules set on this PC apply.",
        PlayTimeDetailTitle = "Your game time today",
        PlayTimeDetailSubtitleFormat = "{0} of {1} used across all games today",
        PlayTimeDetailEmpty =
            "No games played yet today. Guardi counts every game here and stops play once the daily limit is reached.",
        PlayTimeDetailEmptyWithLimitFormat =
            "No games played yet, sweetie — you've still got {0} out of {1} for fun today! Guardi will tuck the games away once your allowance runs out.",
        PlayTimeDetailZeroLimit =
            "Daily limit is 0 minutes — games are blocked for the rest of the day.",
        DetailBlockedAppsTitle = "Apps your Dom said NO to",
        DetailBlockedSitesTitle = "Websites kept off-limits",
        DetailAppsEmptySubtitle = "No naughty apps blocked right now.",
        DetailSitesEmptySubtitle = "No unsafe websites blocked right now.",
        DetailAppsCountFormat = "{0} app(s) blocked for your safety",
        DetailSitesCountFormat = "{0} website(s) blocked for your safety",
        CloseAppTitle = "EduGuard Safety",
        CloseAppStayButton = "No, stay protected",
        CloseAppQuitButton = "Yes, quit Guardi",
        ActivityAppsFormat = "{0} apps open on this protected computer",
        DefaultDomMessage = "No messages yet. Your Dom will tell you what you need to know here.",
        InfractionBlockedSearchDetailFormat = "Tried to search for grown-up stuff ({0}).",
    };

    public static ModeCopySet TrustedSub { get; } = new()
    {
        AppSubtitle = "Your study computer — safely supervised",
        LevelSubtitleFormat = "{0} • Keep up the good work!",
        StandingLabel = "Hi {0} — Guardi is helping you stay focused and safe",
        SecurityZoneTitle = "Study zone — you're protected",
        SecurityZoneBody =
            "This computer is set up for homework, learning, and approved fun. Guardi keeps distractions away so you can do your best.",
        MascotGreeting = "Hi {0}! Ready to learn? Guardi's here to keep your study computer safe.",
        SubNameFallback = "there",
        InfractionsGood = "Great job — no rule breaks today!",
        InfractionsWarning = "Breaking the rules may mean tighter study-time limits from your Dom.",
        PunishmentDeescalationLabel = "Easier rules in",
        BedtimeWarningOneHourTitle = "Bedtime is coming soon",
        BedtimeWarningOneHourMessage =
            "One hour until rest time. Guardi is switching on a warm screen filter to ease your eyes — finish your homework and close extra tabs.",
        BedtimeWarningThirtyTitle = "Thirty minutes till rest time",
        BedtimeWarningThirtyMessage =
            "Half an hour left before bedtime. Wrap up what you're doing so tomorrow's school day starts fresh.",
        BedtimeWarningFiveTitle = "Five minutes — almost rest time!",
        BedtimeWarningFiveMessage =
            "Five minutes, then lights out. Save your work and say goodnight to the screen.",
        BedtimeLockHeadline = "Rest time, {0} — good work today!",
        BedtimeLockBody =
            "Bedtime! Your study computer is resting now so you can sleep well. Guardi sealed the screen until wake-up time.",
        BedtimeLockFooter =
            "This stays locked until your Dom's wake-up time — sweet dreams!",
        BedtimeLockCountdownLabel = "Guardi unlocks your study computer in",
        ScreenTimeLockHeadline = "Screen time finished, {0}",
        ScreenTimeLockBody =
            "You've used today's screen allowance. Ask your Dom nicely for a little more, or come back tomorrow refreshed.",
        ScreenTimeLockStatsLabelFormat = "{0} daily study limit",
        ScreenTimeLockFooter =
            "This computer stays supervised until your Dom allows more time.",
        PlayTimeLimitReachedTitle = "Play time is over for today",
        PlayTimeLimitReachedMessageFormat =
            "You've used today's game allowance ({0}). Guardi closed the games — homework first, play later!",
        GameSessionStartTitle = "Good gaming session!",
        GameSessionStartMessageFormat =
            "Have fun with {0} — Guardi's tracking your play allowance.",
        StudyTimeBlockedTitle = "Study time — games wait!",
        StudyTimeBlockedMessageFormat =
            "It's study time! {0} can wait until your focus window ends. Guardi tucked it away so you can concentrate.",
        AppBlockedDomTitle = "Not during study time",
        AppBlockedDomMessageFormat =
            "{0} isn't allowed right now on your study computer. Your Dom wants you focused — Guardi helped out.",
        EnrollmentBody =
            "Your Dom made a link code. Enter it so Guardi can protect your study computer.",
        CloseAppWarning =
            "This study computer is supervised. Are you sure you want to turn Guardi off?",
        DetailAppsEmpty = "No blocked apps right now — keep up the good work!",
        DetailSitesEmpty = "No blocked websites right now — stay on approved pages!",
        ScreenTimeTitle = "Today's screen time",
        ScreenTimeLimitSuffix = "daily allowance",
        InfractionsTitle = "Rule reminders",
        BedtimeTitle = "Rest schedule",
        DomMessageTitle = "Message from your Dom",
        DomMessageDismissButton = "Got it!",
        RestrictionsTitle = "Study & safety rules",
        ActivityTitle = "What you're working on",
        ActivityLogTitle = "Activity log",
        PlayTimeDetailTotalLabel = "Total play time today",
        GameListIconGlyph = "🎮",
        VpnShieldTitle = "No sneaky tunnels",
        VpnShieldActive = "VPN tunnels are blocked — stay visible and safe",
        VpnShieldInactive = "VPN shield is off — ask your Dom to relink",
        PlayTimeTileTitle = "Play time",
        PlayTimeTileUsageFormat = "{0} of {1} play today — tap for details",
        PlayTimeTileZeroLimit = "0 min daily limit — play time blocked",
        StudyTimeTileTitle = "Study time",
        StudyTimeTileActive = "Study mode active — distractions blocked",
        StudyTimeTileInactive = "Normal rules outside study hours",
        TaskManagerBlockTitle = "Task Manager",
        TaskManagerBlockActive = "Task Manager is blocked in this mode",
        TaskManagerBlockInactive = "Task Manager is allowed",
        ProcessKillerBlockTitle = "Process killers",
        ProcessKillerBlockActive = "Auto process-killer tools are blocked to protect Guardi",
        ProcessKillerBlockInactive = "Auto process-killer tools are allowed",
        DomWatchingTitle = "Your Dom can check in",
        DomWatchingDescription = "Your Dom can see activity to help you stay on track",
        SafeSearchTitle = "SafeSearch for school",
        SafeSearchActive = "Grown-up searches are filtered in your browsers",
        SafeSearchInactive = "SafeSearch could not be locked",
        SafeSearchNoAdmin = "SafeSearch needs administrator rights",
        YouTubeRestrictedModeTitle = "YouTube restricted mode",
        YouTubeRestrictedModeActive = "Strict YouTube filtering is locked in Chrome, Edge and Brave",
        YouTubeRestrictedModeInactive = "YouTube restricted mode is off",
        YouTubeRestrictedModeNoAdmin = "YouTube restricted mode needs administrator rights",
        BlockedAppsTitle = "Blocked apps",
        BlockedSitesTitle = "Blocked websites",
        BlockedAppsNone = "No apps blocked — nice!",
        BlockedSitesNone = "No sites blocked — stay on task!",
        BlockedAppsTapFormat = "{0} app(s) blocked — tap to see",
        BlockedSitesTapFormat = "{0} site(s) blocked — tap to see",
        IconVpn = "🛡️",
        IconPlayTime = "🎮",
        IconStudy = "📚",
        IconTaskManager = "📋",
        IconDomWatching = "👀",
        IconSafeSearch = "🔍",
        IconYouTubeRestrictedMode = "▶️",
        IconBlockedApps = "🚫",
        IconBlockedSites = "🌐",
        EnrollmentTitle = "Link your study computer",
        EnrollmentLevelLabel = "Supervision mode:",
        EnrollmentCodeLabel = "Link code",
        EnrollmentNameLabel = "Computer nickname",
        EnrollmentButton = "Start my protected session",
        EnrollmentConnecting = "Guardi is connecting you to your Dom…",
        EnrollmentFailed = "That code didn't work. Ask your Dom for a fresh one.",
        OnlineProtected = "Protected & ready to learn",
        OnlineSyncing = "Reconnecting…",
        StatusSupervisionActive = "Study supervision active",
        StatusConnectionIssue = "Connection hiccup",
        StatusStarting = "Getting your study zone ready…",
        StatusOffline = "Not linked yet",
        StatusSessionEnded = "Session paused — ask your Dom to link again",
        SupervisionStatusInactive = "Supervision inactive",
        SupervisionStatusStarting = "Supervision starting…",
        SupervisionStatusActive = "Supervision active",
        SupervisionStatusOffline = "Rules active — dashboard offline",
        SupervisionBadgeInactive = "OFF",
        SupervisionBadgeStarting = "…",
        SupervisionBadgeActive = "ON",
        SupervisionBadgeOffline = "LOC",
        TodayRulesTitle = "Today's rules",
        TodayRulesModeLabel = "Supervision mode",
        TodayRulesModeEscalatedFormat = "{0} (escalated from {1})",
        TodayRulesScreenLabel = "Screen time",
        TodayRulesScreenRemainingFormat = "{0} left of {1}",
        TodayRulesScreenExhausted = "Daily limit reached",
        TodayRulesGamingLabel = "Gaming",
        TodayRulesGamingRemainingFormat = "{0} left of {1}",
        TodayRulesGamingZeroLimit = "Blocked all day (0 min)",
        TodayRulesYoutubeLabel = "YouTube",
        TodayRulesYoutubeRemainingFormat = "{0} left of {1}",
        TodayRulesYoutubeZeroLimit = "Blocked all day (0 min)",
        TodayRulesStudyLabel = "Study time",
        TodayRulesBedtimeLabel = "Bedtime",
        TodayRulesBedtimeOff = "No bedtime tonight",
        ConnectionLinkedTitle = "Linked to your guardian",
        ConnectionLinkedBody = "Rules sync from the dashboard. Stay on track.",
        ConnectionOfflineTitle = "Dashboard offline",
        ConnectionOfflineBody = "Your Dom's rules still run on this PC until the link returns.",
        ConnectionStartingTitle = "Connecting to your Dom…",
        ConnectionStartingBody = "Guardi is opening the dashboard link.",
        ConnectionLocalTitle = "Local supervision",
        ConnectionLocalBody = "Web dashboard paused — only rules set on this PC apply.",
        PlayTimeDetailTitle = "Play time today",
        PlayTimeDetailSubtitleFormat = "{0} of {1} used across all games",
        PlayTimeDetailEmpty = "No games yet today. Guardi tracks play time so homework comes first.",
        PlayTimeDetailEmptyWithLimitFormat =
            "No gaming sessions yet — {0} of {1} still in today's allowance. Finish your priorities first; Guardi tracks every minute and closes games at the limit.",
        PlayTimeDetailZeroLimit =
            "Daily limit is 0 minutes — games are blocked for the rest of the day.",
        DetailBlockedAppsTitle = "Apps blocked for focus",
        DetailBlockedSitesTitle = "Sites kept off-limits",
        DetailAppsEmptySubtitle = "Nothing blocked right now.",
        DetailSitesEmptySubtitle = "Nothing blocked right now.",
        DetailAppsCountFormat = "{0} app(s) blocked",
        DetailSitesCountFormat = "{0} site(s) blocked",
        CloseAppTitle = "Turn off Guardi?",
        CloseAppStayButton = "Stay protected",
        CloseAppQuitButton = "Quit Guardi",
        ActivityAppsFormat = "{0} apps open on this study computer",
        DefaultDomMessage = "No messages yet. Your Dom will post reminders here.",
        InfractionBlockedSearchDetailFormat = "Tried a blocked search during study time ({0}).",
    };

    public static ModeCopySet RestrictedSub { get; } = new()
    {
        AppSubtitle = "Maximum security — Guardi locked tight",
        LevelSubtitleFormat = "{0} • Strict safety zone",
        StandingLabel = "Hi {0} — 🔒 Guardi sealed this computer — every rule is locked on",
        SecurityZoneTitle = "🔒 Maximum security zone!",
        SecurityZoneBody =
            "Sweetie, this device is on super-strict lockdown. Guardi watches everything, blocks the bad stuff, and keeps every door locked for your Dom.",
        MascotGreeting = "Hi {0}! Guardi locked everything tight for you — stay in the safe zone!",
        SubNameFallback = "sweetie",
        InfractionsGood = "Good little one — no infractions! Guardi is proud.",
        InfractionsWarning = "Any rule break can trigger tighter locks from your Dom.",
        PunishmentDeescalationLabel = "Locks ease in",
        BedtimeWarningOneHourTitle = "🔒 Bedtime lock coming, little one",
        BedtimeWarningOneHourMessage =
            "One hour until Guardi seals this computer for sleepy time. A warm screen filter is on now — close everything and get ready for bed!",
        BedtimeWarningThirtyTitle = "🔒 Thirty minutes till lockdown bedtime",
        BedtimeWarningThirtyMessage =
            "Half an hour left! Guardi is getting the locks ready. Wrap up — your Dom wants you in bed soon.",
        BedtimeWarningFiveTitle = "🔒 Five minutes — locks engaging!",
        BedtimeWarningFiveMessage =
            "Five tiny minutes, then Guardi bolts the screen shut for the night. No peeking!",
        BedtimeLockHeadline = "🔒 Sealed for bedtime, {0}",
        BedtimeLockBody =
            "Bedtime lock is ON! Guardi clicked every lock shut — this screen stays sealed until your Dom's wake-up time.",
        BedtimeLockFooter =
            "Only your Dom holds the keys. Guardi won't let you sneak past.",
        BedtimeLockCountdownLabel = "🔒 Guardi unlocks in",
        ScreenTimeLockHeadline = "🔒 Screen time locked out, {0}!",
        ScreenTimeLockBody =
            "All done for today, little one! Guardi bolted the screen — ask your Dom nicely if you need more time.",
        ScreenTimeLockStatsLabelFormat = "{0} strict daily limit",
        ScreenTimeLockFooter =
            "This device stays locked until your Dom opens it up again.",
        PlayTimeLimitReachedTitle = "🔒 Play time locked!",
        PlayTimeLimitReachedMessageFormat =
            "Play time is sealed off ({0} cap reached). Guardi closed your games and locked them tight.",
        GameSessionStartTitle = "🔒 Play time started",
        GameSessionStartMessageFormat =
            "Guardi's watching {0}. Stay inside your limits, little one.",
        StudyTimeBlockedTitle = "🔒 Study lock — no games!",
        StudyTimeBlockedMessageFormat =
            "Study lock is active, sweetie! {0} is sealed away until focus time ends.",
        AppBlockedDomTitle = "🔒 Locked — your Dom said no",
        AppBlockedDomMessageFormat =
            "Nope, little one! {0} is on the locked list. Guardi sealed it shut for your Dom.",
        EnrollmentBody =
            "Enter your Dom's code to activate Guardi's maximum security mode.",
        CloseAppWarning =
            "Maximum security is active. Turning Guardi off removes all locks — are you sure?",
        DetailAppsEmpty = "Extra apps locked by your Dom appear here.",
        DetailSitesEmpty = "Extra sites locked by your Dom appear here.",
        ScreenTimeTitle = "🔒 Screen allowance",
        ScreenTimeLimitSuffix = "strict daily limit",
        InfractionsTitle = "🔒 Safety infractions",
        BedtimeTitle = "🔒 Bedtime lock",
        DomMessageTitle = "Important lockdown message",
        DomMessageDismissButton = "Understood!",
        RestrictionsTitle = "🔒 Locked safety rules",
        ActivityTitle = "What Guardi is watching",
        ActivityLogTitle = "Security log",
        PlayTimeDetailTotalLabel = "Play time used today",
        GameListIconGlyph = "🔒",
        VpnShieldTitle = "🔒 VPN sealed shut",
        VpnShieldActive = "VPN tunnels are locked — no sneaking past Guardi!",
        VpnShieldInactive = "VPN lock is off — tell your Dom!",
        PlayTimeTileTitle = "🔒 Play time cap",
        PlayTimeTileUsageFormat = "{0} of {1} used — tap for details",
        PlayTimeTileZeroLimit = "0 min daily limit — play blocked",
        StudyTimeTileTitle = "Study time",
        StudyTimeTileActive = "Study mode active — distractions blocked",
        StudyTimeTileInactive = "Normal rules outside study hours",
        TaskManagerBlockTitle = "🔒 Task Manager locked",
        TaskManagerBlockActive = "Task Manager is sealed in restricted mode",
        TaskManagerBlockInactive = "Task Manager allowed",
        ProcessKillerBlockTitle = "🔒 Process killers sealed",
        ProcessKillerBlockActive = "Auto process-killer tools are shut down instantly",
        ProcessKillerBlockInactive = "Auto process-killer tools allowed",
        DomWatchingTitle = "🔒 Dom is watching",
        DomWatchingDescription = "Everything is visible to your Dom — Guardi makes sure",
        SafeSearchTitle = "🔒 SafeSearch locked",
        SafeSearchActive = "Grown-up searches are locked and filtered",
        SafeSearchInactive = "SafeSearch lock failed",
        SafeSearchNoAdmin = "SafeSearch needs administrator rights",
        YouTubeRestrictedModeTitle = "🔒 YouTube restricted mode",
        YouTubeRestrictedModeActive = "Strict YouTube filtering is locked in Chrome, Edge and Brave",
        YouTubeRestrictedModeInactive = "YouTube restricted mode is off",
        YouTubeRestrictedModeNoAdmin = "YouTube restricted mode needs administrator rights",
        BlockedAppsTitle = "🔒 Blocked apps",
        BlockedSitesTitle = "🔒 Blocked sites",
        BlockedAppsNone = "No extra locked apps",
        BlockedSitesNone = "No extra locked sites",
        BlockedAppsTapFormat = "{0} app(s) locked — tap to see",
        BlockedSitesTapFormat = "{0} site(s) locked — tap to see",
        IconVpn = "🔒",
        IconPlayTime = "🔒",
        IconStudy = "🔒",
        IconTaskManager = "🔒",
        IconDomWatching = "🔒",
        IconSafeSearch = "🔒",
        IconYouTubeRestrictedMode = "🔒",
        IconBlockedApps = "🔒",
        IconBlockedSites = "🔒",
        EnrollmentTitle = "Activate maximum security",
        EnrollmentLevelLabel = "Lockdown mode:",
        EnrollmentCodeLabel = "Secret link code",
        EnrollmentNameLabel = "Computer nickname",
        EnrollmentButton = "Seal & protect this computer",
        EnrollmentConnecting = "Guardi is locking everything down…",
        EnrollmentFailed = "That code didn't work. Ask your Dom.",
        OnlineProtected = "🔒 Locked & linked",
        OnlineSyncing = "Reconnecting security…",
        StatusSupervisionActive = "🔒 Maximum security active",
        StatusConnectionIssue = "Security link interrupted",
        StatusStarting = "Engaging locks…",
        StatusOffline = "Not sealed yet",
        StatusSessionEnded = "Lockdown paused — re-link from your Dom",
        SupervisionStatusInactive = "Supervision inactive",
        SupervisionStatusStarting = "Supervision starting…",
        SupervisionStatusActive = "🔒 Supervision active",
        SupervisionStatusOffline = "🔒 Rules active — dashboard offline",
        SupervisionBadgeInactive = "OFF",
        SupervisionBadgeStarting = "…",
        SupervisionBadgeActive = "ON",
        SupervisionBadgeOffline = "LOC",
        TodayRulesTitle = "Today's rules",
        TodayRulesModeLabel = "Supervision mode",
        TodayRulesModeEscalatedFormat = "🔒 {0} (locked up from {1})",
        TodayRulesScreenLabel = "Screen time",
        TodayRulesScreenRemainingFormat = "{0} left of {1}",
        TodayRulesScreenExhausted = "Daily limit reached",
        TodayRulesGamingLabel = "Gaming",
        TodayRulesGamingRemainingFormat = "{0} left of {1}",
        TodayRulesGamingZeroLimit = "Blocked all day (0 min)",
        TodayRulesYoutubeLabel = "YouTube",
        TodayRulesYoutubeRemainingFormat = "{0} left of {1}",
        TodayRulesYoutubeZeroLimit = "Blocked all day (0 min)",
        TodayRulesStudyLabel = "Study time",
        TodayRulesBedtimeLabel = "Bedtime",
        TodayRulesBedtimeOff = "No bedtime tonight",
        ConnectionLinkedTitle = "🔒 Linked to your Dom",
        ConnectionLinkedBody = "Rules sync live from the dashboard — every lock is active.",
        ConnectionOfflineTitle = "Dashboard offline",
        ConnectionOfflineBody = "Your Dom's rules still apply on this PC until the link comes back.",
        ConnectionStartingTitle = "Connecting to your Dom…",
        ConnectionStartingBody = "Guardi is sealing the safety link — one moment.",
        ConnectionLocalTitle = "Local supervision",
        ConnectionLocalBody = "Web dashboard paused — only rules set on this PC apply.",
        PlayTimeDetailTitle = "Play time today",
        PlayTimeDetailSubtitleFormat = "{0} of {1} used — limits enforced",
        PlayTimeDetailEmpty = "No games today. Guardi enforces every limit strictly.",
        PlayTimeDetailEmptyWithLimitFormat =
            "🔒 Nothing logged yet, little one — {0} of {1} still unlocked on your play cap. Guardi watches every second and seals games shut when time runs out.",
        PlayTimeDetailZeroLimit =
            "Daily limit is 0 minutes — games stay blocked all day.",
        DetailBlockedAppsTitle = "🔒 Locked apps",
        DetailBlockedSitesTitle = "🔒 Locked websites",
        DetailAppsEmptySubtitle = "No extra locked apps.",
        DetailSitesEmptySubtitle = "No extra locked sites.",
        DetailAppsCountFormat = "{0} locked app(s)",
        DetailSitesCountFormat = "{0} locked site(s)",
        CloseAppTitle = "Turn off Guardi's locks?",
        CloseAppStayButton = "Stay locked & safe",
        CloseAppQuitButton = "Quit anyway",
        ActivityAppsFormat = "{0} apps open under Guardi's watch",
        DefaultDomMessage = "Guardi is watching. Your Dom will message you here.",
        InfractionBlockedSearchDetailFormat = "Tried a forbidden search ({0}) — Guardi locked it and told your Dom!",
    };

    public static ModeCopySet ForSlug(string slug) => AgentModeSlugs.Normalize(slug) switch
    {
        AgentModeSlugs.TrustedSub => TrustedSub,
        AgentModeSlugs.RestrictedSub => RestrictedSub,
        _ => Sub,
    };
}
