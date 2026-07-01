namespace EduGuardAgent.Profiles;

internal static class LocalModeCopy
{
    public const string BannerTitle = "Local mode";
    public const string BannerBody =
        "Web dashboard settings are paused. Only settings you change in Local settings apply until you exit local mode.";

    public const string EnterButton = "Enter local mode";
    public const string ExitButton = "Exit local mode & sync with web";
    public const string ManageButton = "Local settings";
    public const string StatusLabel = "Local mode — web settings ignored";

    public const string AutoStartLabel = "Open Guardi at Windows sign-in";
    public const string AutoStartDescription =
        "When enabled, Guardi creates a Windows logon task and relaunches automatically after sign-in. Turn this off to stop opening at startup.";
    public const string WidgetRemindersTitle = "Desktop reminders above the widget";
    public const string WidgetRemindersHint =
        "Short motivational reminders appear above the desktop widget while supervision is active. Messages rotate in a random order.";
    public const string WidgetRemindersEnabledLabel = "Show desktop reminders";
    public const string WidgetRemindersFrequencyLabel = "Reminder frequency (minutes)";

    public const string HubTitle = "Local settings";
    public const string HubSubtitle =
        "Manage this PC like the Dom dashboard. Rules are saved per supervision mode.";

    public const string EditingModeLabel = "MODE";
    public const string ActiveModeLabel = "ACTIVE ON THIS PC";
    public const string ActiveModeSaveHint = "Select a mode, then press Apply to switch this PC.";
    public const string ActiveModeApplyButton = "Apply";

    public const string BedtimeEnabled = "Bedtime enabled";
    public const string BedtimeTime = "Default bedtime (HH:mm)";
    public const string BedtimeWeeklyTitle = "Per-day bedtime overrides";
    public const string BedtimeWeeklyHint =
        "Optional. Toggle a day to set its own bedtime and wake-up times.";
    public const string BlueLightFilterEnabled = "Warm screen tone (1 h before bed)";
    public const string WakeTime = "Default wake-up (HH:mm)";

    public const string StudyEnabled = "Study time enabled";
    public const string StudyStart = "Default start (HH:mm)";
    public const string StudyEnd = "Default end (HH:mm)";
    public const string StudyDays = "Default days (mon,tue,...)";
    public const string StudyWeeklyTitle = "Per-day study overrides";
    public const string StudyWeeklyHint =
        "Optional. Toggle a day to set its own start and end times.";
    public const string StudyBlockGames = "Block games";
    public const string StudyBlockYoutube = "Block YouTube";
    public const string YoutubeRestrictedModeEnabled = "YouTube restricted mode";
    public const string YoutubeRestrictedModeHint =
        "Locks strict YouTube filtering in Chrome, Edge and Brave (browser policy). Off by default.";
    public const string StudyBlockDistractingSites = "Block distracting sites";
    public const string StudyBlockDistractingApps = "Block distracting apps";

    public const string ImageShieldEnabled = "Enable image shield";
    public const string ImageShieldHint =
        "Blurs inappropriate images on-device in the browser. Nothing leaves this PC.";
    public const string ImageShieldPerModeTitle = "Per supervision mode";
    public const string ImageShieldPerModeHint = "Choose which supervision levels use image shield.";
    public const string ImageShieldPerBrowserTitle = "Per browser";
    public const string ImageShieldPerBrowserHint =
        "Chrome: Web Store force-install (personal Windows) or --load-extension for local dist/chromium. Edge/Brave share the same policy.";
    public const string ImageShieldAdvancedTitle = "Sensitivity";
    public const string ImageShieldMinSizeLabel = "Min image size (px)";
    public const string ImageShieldThresholdLabel = "NSFW threshold (0.4 strict … 0.8 loose)";
    public const string ImageShieldMaxPerSecondLabel = "Max classifications per second";
    public const string ImageShieldRuntimeStatusTitle = "Runtime status";
    public const string ImageShieldCopyReportButton = "Copy shield report";
    public const string ImageShieldCopyReportHint =
        "Policies, install method, versions and recent audit lines — for Dom support or debugging.";
    public const string ImageShieldCopyReportDone = "Shield diagnostic copied to clipboard.";
    public const string ImageShieldFirefoxDevHint =
        "Requires Firefox Developer Edition (or sideload via about:debugging). Guardi must run as administrator.";
    public const string BlockUrlButton = "Block site";
    public const string BlockAppButton = "Block app";

    public const string AppLimitsExeLabel = "App executable";
    public const string AppLimitsExeHint = "Process name, e.g. discord.exe or chrome.exe";
    public const string AppLimitsMinutesLabel = "Daily limit (minutes)";
    public const string AppLimitsAddButton = "Add limit";
    public const string AppLimitsListTitle = "Configured limits";
    public const string AppLimitsListHint = "Time counts while the app is in the foreground. Saved per supervision mode.";
    public const string AppLimitInvalidExe = "Enter a valid app name (letters, numbers, spaces, .exe).";

    public const string EnabledLog = "Local mode on — web settings are ignored.";
    public const string DisabledLog = "Local mode off.";
    public const string SyncingLog = "Syncing with the web dashboard…";
    public const string SyncedLog = "Synced with the web dashboard.";
    public const string SyncFailedKeepLocal =
        "Could not reach the web dashboard — local mode stays on and your local settings are still active.";

    public const string PinRequiredForSettings = "Enter your exit PIN to open local settings.";
    public const string SectionSavedLog = "Local settings saved.";
    public const string KioskAppsTitle = "Detected apps on this PC";
    public const string KioskAppsHint =
        "Common apps found on this computer are whitelisted by default. Toggling a switch saves instantly — no separate save step.";
    public const string KioskAppsAddTitle = "Add an app manually";
    public const string KioskAppsAddHint = "Pick a .exe on this PC or paste its full path.";
    public const string KioskAppsPathLabel = "Executable path (.exe)";
    public const string KioskAppsNameLabel = "Display name (optional)";
    public const string KioskAppsBrowseButton = "Browse…";
    public const string KioskAppsAddButton = "Add app";
    public const string KioskAppsRefreshButton = "Scan again";
    public const string KioskAppsEmpty = "No common apps were detected on this PC.";
    public const string KioskAppsSavedLog = "Kiosk app list saved.";
    public const string KioskAppsAddedLog = "Added {0} to kiosk apps.";
    public const string KioskAppsInvalidPath = "Enter a valid .exe path that exists on this PC.";
    public const string KioskAppsDuplicate = "{0} is already in the kiosk app list.";
    public const string InfractionKindsTitle = "What counts as an infraction";
    public const string InfractionKindsHint = "Disabled kinds are ignored — no count and no escalation.";
    public const string ClearPunishmentButton = "Clear punishment";
    public const string ClearPunishmentLog = "Punishment cleared — infraction count and escalation reset.";
}

