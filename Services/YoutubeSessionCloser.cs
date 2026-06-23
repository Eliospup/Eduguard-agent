using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal static class YoutubeSessionCloser
{
    private const int WM_CLOSE = 0x0010;

    public static bool TryClose(DetectedYoutubeSession session)
    {
        if (session.IsDedicatedApp)
            return TryKillProcess(session.ProcessId);

        if (session.WindowHandle != IntPtr.Zero && IsWindow(session.WindowHandle))
        {
            if (PostMessage(session.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            {
                AuditLog.Write(
                    $"YouTube limit: closed {session.SourceLabel} app window (dedicated app).");
                return true;
            }
        }

        return TryKillProcess(session.ProcessId);
    }

    private static bool TryKillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            AuditLog.Write($"YouTube limit: closed process {processId} (fallback kill).");
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}
