using EduGuardAgent.Models;

namespace EduGuardAgent.Profiles;

internal static class BedtimeMessages
{
    public static (string Title, string Message) Warning(BedtimeWarningKind kind) =>
        kind switch
        {
            BedtimeWarningKind.OneHour => (UiCopy.BedtimeWarningOneHourTitle, UiCopy.BedtimeWarningOneHourMessage),
            BedtimeWarningKind.ThirtyMinutes => (UiCopy.BedtimeWarningThirtyTitle, UiCopy.BedtimeWarningThirtyMessage),
            BedtimeWarningKind.FiveMinutes => (UiCopy.BedtimeWarningFiveTitle, UiCopy.BedtimeWarningFiveMessage),
            _ => (UiCopy.BedtimeWarningFiveTitle, UiCopy.BedtimeWarningFiveMessage),
        };
}
