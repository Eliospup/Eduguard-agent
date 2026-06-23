using System.Globalization;

namespace EduGuardAgent.Services;

internal static class AgentShieldStateFactory
{
    public static Models.AgentShieldStateDto FromSettings(
        bool active,
        ImageShieldSettings settings,
        IReadOnlyDictionary<string, bool>? browserActive = null,
        string? subDisplayName = null)
    {
        var culture = CultureInfo.InvariantCulture;
        var browsers = browserActive is null
            ? new Dictionary<string, bool>(StringComparer.Ordinal)
            : new Dictionary<string, bool>(browserActive, StringComparer.Ordinal);

        var managed = new Dictionary<string, object>
        {
            ["shieldActive"] = active && settings.ShieldActive,
            ["minSize"] = settings.MinSize,
            ["maxPerSecond"] = settings.MaxPerSecond,
            ["nsfwThreshold"] = settings.NsfwThreshold.ToString("0.##", culture),
            ["thumbThreshold"] = settings.ThumbThreshold.ToString("0.##", culture),
            ["sexyWeight"] = settings.SexyWeight.ToString("0.##", culture),
            ["uiMode"] = settings.UiMode,
            ["browserActive"] = browsers,
        };

        if (!string.IsNullOrWhiteSpace(subDisplayName))
            managed["displayName"] = subDisplayName.Trim();

        if (browsers.TryGetValue(ImageShieldBrowserKeys.Firefox, out var firefoxActive))
            managed["firefoxActive"] = firefoxActive;

        return new Models.AgentShieldStateDto(
            AgentRunning: true,
            Active: active,
            Managed: managed);
    }

    public static Models.AgentShieldStateDto WithDisplayName(
        Models.AgentShieldStateDto state,
        string? subDisplayName)
    {
        if (string.IsNullOrWhiteSpace(subDisplayName))
            return state;

        var managed = new Dictionary<string, object>(state.Managed, StringComparer.Ordinal)
        {
            ["displayName"] = subDisplayName.Trim(),
        };
        return state with { Managed = managed };
    }
}
