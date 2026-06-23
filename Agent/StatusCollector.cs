using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EduGuardAgent.Agent;

internal static class StatusCollector
{
    private const int IdleThresholdSeconds = 60;

    public static Models.HeartbeatRequest Collect(string level)
    {
        return new Models.HeartbeatRequest
        {
            FocusedWindow = GetFocusedWindowTitle(),
            RunningApps = GetRunningApps(),
            IsIdle = IsUserIdle(),
            Level = level,
        };
    }

    private static string GetFocusedWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return "(none)";

        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return "(untitled)";

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        var title = builder.ToString();
        return string.IsNullOrWhiteSpace(title) ? "(untitled)" : title;
    }

    private static IReadOnlyList<string> GetRunningApps()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(process.ProcessName))
                    continue;

                names.Add(process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? process.ProcessName
                    : $"{process.ProcessName}.exe");
            }
            catch
            {
                // Access denied for some system processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsUserIdle()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return false;

        var idleMs = Environment.TickCount64 - info.DwTime;
        return idleMs >= IdleThresholdSeconds * 1000;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }
}
