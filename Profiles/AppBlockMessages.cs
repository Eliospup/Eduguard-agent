using EduGuardAgent.Models;

namespace EduGuardAgent.Profiles;

internal static class AppBlockMessages
{
    public static (string Title, string Message) Format(AppBlockCategory category, string appDisplayName) =>
        category switch
        {
            AppBlockCategory.VpnShield => (
                UiCopy.AppBlockedVpnTitle,
                string.Format(UiCopy.AppBlockedVpnMessage, appDisplayName)),
            AppBlockCategory.DomImmediate => (
                UiCopy.AppBlockedDomImmediateTitle,
                string.Format(UiCopy.AppBlockedDomImmediateMessage, appDisplayName)),
            AppBlockCategory.DomManual => (
                UiCopy.AppBlockedDomTitle,
                UiCopy.AppBlockedDomMessageFormat(appDisplayName)),
            AppBlockCategory.StudyTime => (
                UiCopy.StudyTimeBlockedTitle,
                UiCopy.StudyTimeBlockedMessageFormat(appDisplayName)),
            _ => (
                UiCopy.AppBlockedDefaultTitle,
                string.Format(UiCopy.AppBlockedDefaultMessage, appDisplayName)),
        };
}
