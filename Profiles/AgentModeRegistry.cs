using EduGuardAgent.Models;

namespace EduGuardAgent.Profiles;

internal static class AgentModeRegistry
{
    public static AgentModeDefinition TrustedSub { get; } = new()
    {
        Slug = AgentModeSlugs.TrustedSub,
        DisplayName = "Trusted Sub",
        ShortLabel = "TS",
        Copy = ModeCopies.TrustedSub,
        Theme = ModeTheme.TrustedSub,
        Ui = ModeUiPresentation.Study,
        ModeSubtitle = "Study supervision • reassuring & focused",
        Defaults = new ModeRuleDefaults
        {
            ScreenTimeLimitMinutes = 480,
            GamingTimeLimitMinutes = 120,
            YoutubeTimeLimitMinutes = 60,
            BedtimeEnabled = false,
            StudyTimeEnabled = false,
        },
        Features = ModeFeatures.ForTrustedSub,
    };

    public static AgentModeDefinition Sub { get; } = new()
    {
        Slug = AgentModeSlugs.Sub,
        DisplayName = "Sub",
        ShortLabel = "SB",
        Copy = ModeCopies.Sub,
        Theme = ModeTheme.Sub,
        Ui = ModeUiPresentation.Playful,
        ModeSubtitle = "Full Guardi mode • cozy & very supervised",
        Defaults = new ModeRuleDefaults
        {
            ScreenTimeLimitMinutes = 300,
            GamingTimeLimitMinutes = 60,
            YoutubeTimeLimitMinutes = 30,
            BedtimeEnabled = true,
            StudyTimeEnabled = false,
        },
        Features = ModeFeatures.ForSub,
    };

    public static AgentModeDefinition RestrictedSub { get; } = new()
    {
        Slug = AgentModeSlugs.RestrictedSub,
        DisplayName = "Restricted Sub",
        ShortLabel = "RS",
        Copy = ModeCopies.RestrictedSub,
        Theme = ModeTheme.RestrictedSub,
        Ui = ModeUiPresentation.SecurePlayful,
        ModeSubtitle = "Maximum security • locked-down Guardi",
        Defaults = new ModeRuleDefaults
        {
            ScreenTimeLimitMinutes = 180,
            GamingTimeLimitMinutes = 30,
            YoutubeTimeLimitMinutes = 15,
            BedtimeEnabled = true,
            StudyTimeEnabled = true,
        },
        Features = ModeFeatures.ForRestrictedSub,
    };

    /// <summary>Modes ordered from the most permissive to the strictest.</summary>
    public static IReadOnlyList<AgentModeDefinition> All { get; } =
        [TrustedSub, Sub, RestrictedSub];

    /// <summary>Highest valid strictness index in <see cref="All"/>.</summary>
    public static int MaxStrictnessIndex => All.Count - 1;

    public static AgentModeDefinition Get(string? slug) =>
        All.FirstOrDefault(mode => string.Equals(mode.Slug, AgentModeSlugs.Normalize(slug), StringComparison.Ordinal))
        ?? Sub;

    /// <summary>Strictness rank of a mode (0 = most permissive). Unknown slugs map to 0.</summary>
    public static int StrictnessIndex(string? slug)
    {
        var normalized = AgentModeSlugs.Normalize(slug);
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Slug, normalized, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    public static AgentModeDefinition AtStrictnessIndex(int index) =>
        All[Math.Clamp(index, 0, MaxStrictnessIndex)];
}
