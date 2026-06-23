using EduGuardAgent.Agent;
using EduGuardAgent.Models;

namespace EduGuardAgent.Services;

/// <summary>Applies a heartbeat <c>settings</c> payload from the Dom dashboard.</summary>
internal static class ServerSettingsApplier
{
    public static void Apply(IAgentNotifier notifier, AgentSettingsPayload settings)
    {
        if (settings.Mode is not null || settings.ScreenTime is not null)
        {
            var mode = settings.Mode ?? new ModeSettingsPayload();
            notifier.ModeSettingsReceived(mode, settings.ScreenTime, domOverride: false);
        }

        if (settings.Bedtime is { } bedtime)
            notifier.BedtimeSettingsReceived(bedtime);

        if (settings.TryGetExitPin(out var exitPin))
            notifier.ExitPinReceived(exitPin);

        if (settings.Gaming is { } gaming)
            notifier.GamingSettingsReceived(gaming, replaceGameLists: false);

        if (settings.Youtube is { } youtube)
            notifier.YoutubeSettingsReceived(youtube);

        if (settings.StudyTime is { } studyTime)
            notifier.StudyTimeSettingsReceived(studyTime);

        if (settings.Kiosk is { } kiosk)
            notifier.KioskSettingsReceived(kiosk);

        if (settings.Punishment is { } punishment)
            notifier.PunishmentSettingsReceived(punishment);

        if (settings.ImageShield is { } imageShield)
            notifier.ImageShieldSettingsReceived(imageShield);
    }
}
