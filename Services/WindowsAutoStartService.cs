using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

/// <summary>
/// Registers Guardi in the current user's Windows logon task scheduler so supervision
/// resumes after reboot without a manual launch.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAutoStartService
{
    public const string TaskName = "GuardiAgent";

    /// <summary>Seconds after logon before launching — lets the desktop shell finish starting.</summary>
    private const int LogonDelaySeconds = 15;

    public static bool IsRegistered() => RunSchtasks($"/Query /TN \"{TaskName}\" /FO LIST", out _) == 0;

    public static bool TryEnable(out string? error)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            error = "Could not resolve the Guardi executable path.";
            return false;
        }

        if (!File.Exists(exePath))
        {
            error = $"Guardi executable not found at {exePath}.";
            return false;
        }

        // Guardi requires admin (app.manifest). A LIMITED logon task fails with
        // ERROR_ELEVATION_REQUIRED (0x800702E4). HIGHEST runs elevated at logon without UAC.
        var taskAction = $"\\\"{exePath}\\\"";
        var runAs = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var delay = $"0000:{LogonDelaySeconds:D2}";

        var code = RunSchtasks(
            $"/Create /TN \"{TaskName}\" /TR \"{taskAction}\" /SC ONLOGON /DELAY {delay} /RL HIGHEST /RU \"{runAs}\" /F",
            out var details);

        if (code == 0)
        {
            error = null;
            return true;
        }

        error = string.IsNullOrWhiteSpace(details)
            ? $"schtasks create failed (exit {code})."
            : $"schtasks create failed (exit {code}): {details.Trim()}";
        return false;
    }

    public static bool TryDisable(out string? error)
    {
        if (!IsRegistered())
        {
            error = null;
            return true;
        }

        var code = RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out var details);
        if (code == 0)
        {
            error = null;
            return true;
        }

        error = string.IsNullOrWhiteSpace(details)
            ? $"schtasks delete failed (exit {code})."
            : $"schtasks delete failed (exit {code}): {details.Trim()}";
        return false;
    }

    private static int RunSchtasks(string arguments, out string? details)
    {
        details = null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
                return -1;

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);

            details = string.Concat(stdout, stderr);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            details = ex.Message;
            return -1;
        }
    }
}
