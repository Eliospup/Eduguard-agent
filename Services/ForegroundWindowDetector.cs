using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EduGuardAgent.Services;

internal static class ForegroundWindowDetector
{
    public static bool TryGetForegroundProcessExe(out string exe)
    {
        exe = string.Empty;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (string.IsNullOrWhiteSpace(process.ProcessName))
                return false;

            exe = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : $"{process.ProcessName}.exe";
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
