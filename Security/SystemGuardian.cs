using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// Optional SYSTEM-context guardian, run by a <c>/RU SYSTEM</c> scheduled task. Because it
/// runs as LocalSystem, a supervised user — even an administrator — cannot terminate it
/// without first escalating to SYSTEM (a deliberate, detectable act). It does two things a
/// user-mode process cannot guarantee for itself:
///   1. Re-asserts the protected-state DACL from a context the user cannot kill.
///   2. Resurrects the interactive user agent if it (and its user-mode watchdog) are gone.
///
/// It honors an ACL-protected stand-down marker so a parent's PIN-authorized quit is not
/// immediately undone. Entirely opt-in: nothing installs this task automatically. Removed
/// cleanly via <see cref="TryUninstall"/> / <see cref="SecurityTeardown"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemGuardian
{
    public const string GuardianArg = "--system-guardian";
    public const string TaskName = "GuardiSystem";

    private const string StandDownMarker = "guardian.standdown";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public static bool IsInstalled() => RunSchtasks($"/Query /TN \"{TaskName}\"", out _) == 0;

    /// <summary>Starts the guardian task now if it isn't already running (no-op otherwise).</summary>
    public static void StartIfNotRunning() => RunSchtasks($"/Run /TN \"{TaskName}\"", out _);

    public static bool TryInstall(out string? error)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            error = "Could not resolve the Guardi executable path.";
            return false;
        }

        var taskAction = $"\\\"{exePath}\\\" {GuardianArg}";
        var code = RunSchtasks(
            $"/Create /TN \"{TaskName}\" /TR \"{taskAction}\" /SC ONSTART /RL HIGHEST /RU SYSTEM /F",
            out var details);

        if (code == 0)
        {
            AuditLog.Write("SYSTEM guardian task installed.");
            error = null;
            return true;
        }

        error = string.IsNullOrWhiteSpace(details)
            ? $"schtasks create failed (exit {code})."
            : $"schtasks create failed (exit {code}): {details.Trim()}";
        return false;
    }

    public static bool TryUninstall(out string? error)
    {
        if (!IsInstalled())
        {
            error = null;
            return true;
        }

        var code = RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out var details);
        if (code == 0)
        {
            AuditLog.Write("SYSTEM guardian task removed.");
            error = null;
            return true;
        }

        error = string.IsNullOrWhiteSpace(details)
            ? $"schtasks delete failed (exit {code})."
            : $"schtasks delete failed (exit {code}): {details.Trim()}";
        return false;
    }

    /// <summary>Called by the agent on a PIN-authorized shutdown so the guardian stands down.</summary>
    public static void SignalStandDown()
    {
        try
        {
            StateProtection.Write(SecureDataPaths.PathFor(StandDownMarker), DateTimeOffset.UtcNow.ToString("o"));
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>Called by the agent at startup so the guardian resumes guarding.</summary>
    public static void ClearStandDown()
    {
        try
        {
            StateProtection.Delete(SecureDataPaths.PathFor(StandDownMarker));
        }
        catch
        {
            // Best-effort.
        }
    }

    public static void Run()
    {
        AuditLog.Write("SYSTEM guardian started.");

        // Serve secure-state writes for the agent once lockdown makes the folder SYSTEM-only.
        var ipcCts = new CancellationTokenSource();
        var ipcThread = new Thread(() => SecureStateIpcServer.RunLoop(ipcCts.Token))
        {
            IsBackground = true,
            Name = "GuardiSecureStateIpc",
        };
        ipcThread.Start();

        while (true)
        {
            try
            {
                // If our task was removed (uninstall/teardown), stand down immediately so no
                // SYSTEM process lingers until the next reboot.
                if (!IsInstalled())
                {
                    AuditLog.Write("SYSTEM guardian task gone — exiting.");
                    ipcCts.Cancel();
                    return;
                }

                SecureDataPaths.ReassertAcl();

                if (!StandDownRequested() && !IsAgentRunningInConsole())
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        if (InteractiveProcessLauncher.TryLaunchInActiveSession(exePath, arguments: null, out var error))
                            AuditLog.Write("SYSTEM guardian resurrected the user agent.");
                        else if (error is not null)
                            AuditLog.Write($"SYSTEM guardian resurrection failed: {error}");
                    }
                }
            }
            catch
            {
                // Never let the guardian loop die on a transient error.
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static bool StandDownRequested()
    {
        try
        {
            return File.Exists(SecureDataPaths.PathFor(StandDownMarker));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAgentRunningInConsole()
    {
        var console = InteractiveProcessLauncher.ActiveConsoleSessionId();
        if (console == 0xFFFFFFFF)
            return true; // No interactive session (e.g. logon screen) — nothing to resurrect.

        var exePath = Environment.ProcessPath;
        var name = string.IsNullOrEmpty(exePath)
            ? "EduGuardAgent"
            : Path.GetFileNameWithoutExtension(exePath);

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(name);
        }
        catch
        {
            return true; // Be conservative: don't spawn duplicates on a query failure.
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    // Any of our processes alive in the interactive session (agent or its
                    // user-mode watchdog) means user-mode resurrection is covering things.
                    if (process.Id != Environment.ProcessId && (uint)process.SessionId == console)
                        return true;
                }
                catch
                {
                    // Ignore processes we can't inspect.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

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
