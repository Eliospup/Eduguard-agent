using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EduGuardAgent.Services;

internal readonly record struct DetectedYoutubeSession(
    string SourceLabel,
    int ProcessId,
    string ProcessExe,
    IntPtr WindowHandle,
    bool IsDedicatedApp);

internal static class YoutubeForegroundDetector
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe",
        "msedge.exe",
        "firefox.exe",
        "brave.exe",
        "opera.exe",
        "vivaldi.exe",
        "arc.exe",
        "browser.exe",
    };

    private static readonly HashSet<string> YoutubeProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.exe",
    };

    public static bool TryGetActiveSession(out DetectedYoutubeSession session)
    {
        session = default;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsIconic(hwnd))
            return false;

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
            return false;

        string exe;
        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (string.IsNullOrWhiteSpace(process.ProcessName))
                return false;

            processName = process.ProcessName;
            exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : $"{processName}.exe";
        }
        catch
        {
            return false;
        }

        if (YoutubeProcesses.Contains(exe))
        {
            session = new DetectedYoutubeSession("YouTube", (int)processId, exe, hwnd, IsDedicatedApp: true);
            return true;
        }

        if (!BrowserProcesses.Contains(exe))
            return false;

        var title = GetWindowTitle(hwnd);
        if (!IsYoutubeTitle(title))
            return false;

        session = new DetectedYoutubeSession(ResolveBrowserLabel(exe), (int)processId, exe, hwnd, IsDedicatedApp: false);
        return true;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool IsYoutubeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        if (title.Contains(" - YouTube", StringComparison.OrdinalIgnoreCase))
            return true;

        if (title.Contains(" — YouTube", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(title.Trim(), "YouTube", StringComparison.OrdinalIgnoreCase))
            return true;

        return title.StartsWith("YouTube", StringComparison.OrdinalIgnoreCase)
               && title.Contains("Music", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveBrowserLabel(string exe) => exe.ToLowerInvariant() switch
    {
        "chrome.exe" => "Chrome",
        "msedge.exe" => "Edge",
        "firefox.exe" => "Firefox",
        "brave.exe" => "Brave",
        "opera.exe" => "Opera",
        "vivaldi.exe" => "Vivaldi",
        "arc.exe" => "Arc",
        _ => "Browser",
    };

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
