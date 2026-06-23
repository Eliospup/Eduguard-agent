using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal static class YoutubeSessionBlocker
{
    public static bool TryBlock(DetectedYoutubeSession session, string reason)
    {
        if (session.IsDedicatedApp)
            return YoutubeSessionCloser.TryClose(session);

        AuditLog.Write(
            $"YouTube {reason}: redirecting {session.SourceLabel} to Guardi block page (browser stays open).");
        return true;
    }
}
