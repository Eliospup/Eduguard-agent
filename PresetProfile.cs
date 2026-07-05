using System.Runtime.Versioning;
using EduGuardAgent.Models;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent;

/// <summary>
/// One-time custom build preset. Seeds name, PIN, mode, and all restrictions on first launch
/// so the onboarding wizard can skip straight to a "you're all set" screen.
/// Delete this file (or set <see cref="Enabled"/> to false) for the standard build.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PresetProfile
{
    public static bool Enabled => false;

    public const string SubName = "Boy";
    public const string Pin = "313233";
    public const string Mode = AgentModeSlugs.TrustedSub;

    public static bool IsAlreadyApplied(SubProfileService subProfile) =>
        subProfile.HasDisplayName;

    /// <summary>
    /// Web-content safety categories to block. Enabling "adult" also switches system DNS to the
    /// Cloudflare Family resolver + locks DoH. There's no parent dashboard on this build to turn
    /// these on, so the preset seeds every harmful category.
    /// </summary>
    public static IReadOnlyList<string> WebCategoryKeys =>
        WebCategoryCatalog.All.Select(c => c.Key).ToList();

    public static void Apply(SubProfileService subProfile, ExitPinService exitPin)
    {
        subProfile.TrySetDisplayName(SubName);
        exitPin.SetPin(Pin);
    }

    public static LocalSettingsCatalog BuildCatalog()
    {
        var catalog = LocalSettingsCatalog.CreateDefaults();
        catalog.ActiveModeSlug = Mode;
        catalog.ImageShieldEnabled = true;
        catalog.YouTubeRestrictedModeEnabled = false;
        catalog.BlueLightFilterEnabled = true;
        catalog.PunishmentEnabled = true;

        var weekendDays = new[] { "sat", "sun" };
        var weekdayDays = new[] { "mon", "tue", "wed", "thu", "fri" };

        foreach (var slug in new[] { AgentModeSlugs.TrustedSub, AgentModeSlugs.Sub, AgentModeSlugs.RestrictedSub })
        {
            var rules = catalog.PerMode[slug];

            // Screen time: 4h weekdays, 5h weekends
            rules.ScreenTimeDailyLimitMinutes = 240;
            rules.ScreenTimeWeekly = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in weekdayDays) rules.ScreenTimeWeekly[d] = 240;
            foreach (var d in weekendDays) rules.ScreenTimeWeekly[d] = 300;

            // Gaming: 2h weekdays, 3h weekends
            rules.GamingDailyLimitMinutes = 120;
            rules.GamingWeekly = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in weekdayDays) rules.GamingWeekly[d] = 120;
            foreach (var d in weekendDays) rules.GamingWeekly[d] = 180;
            rules.GamingShowOverlay = true;

            // YouTube: 2h/day
            rules.YoutubeDailyLimitMinutes = 120;
            rules.YoutubeShowOverlay = true;

            // Bedtime: 23:30→5:00 weekdays, 01:00→5:00 weekends
            rules.BedtimeEnabled = true;
            rules.BedtimeTime = "23:30";
            rules.WakeTime = "05:00";
            rules.BedtimeWeekly = new Dictionary<string, LocalBedtimeDayOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in weekendDays)
            {
                rules.BedtimeWeekly[d] = new LocalBedtimeDayOverride
                {
                    Enabled = true,
                    Time = "01:00",
                    WakeTime = "05:00",
                };
            }

            // System tools: all locked
            rules.BlockTaskManager = true;
            rules.VpnShield = true;
            rules.BlockRegistryEditor = true;
            rules.BlockCommandPrompt = true;
            rules.BlockPowerShell = true;
            rules.BlockSystemConfig = true;
            rules.BlockControlPanel = true;
            rules.BlockProcessTools = true;
            rules.BlockProcessKillers = true;

            // Kiosk: only in restricted_sub
            rules.KioskMode = slug == AgentModeSlugs.RestrictedSub;
        }

        return catalog;
    }
}
