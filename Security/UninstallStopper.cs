using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// Cleanly stops the self-protecting Guardi process cluster so an uninstall can proceed.
/// Signals the intentional-shutdown event (which halts mutual resurrection and tells the
/// SYSTEM guardian to stand down), then terminates any remaining Guardi processes — the
/// main agent, the watchdog, and the SYSTEM guardian — from this elevated context.
///
/// Must run BEFORE the footprint revert / data deletion: while any agent process is live it
/// keeps re-asserting hosts, policies and the hardened folder ACL.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UninstallStopper
{
    public static void StopRunningCluster()
    {
        // SeDebugPrivilege lets this elevated process terminate the SYSTEM guardian too.
        TokenPrivilege.TryEnable("SeDebugPrivilege");

        try
        {
            // Stops resurrection + signals the SYSTEM guardian to stand down.
            ProcessGuardian.SignalIntentionalShutdown();
        }
        catch
        {
            // continue
        }

        // Give the watchdog a moment to observe the shutdown signal and self-exit.
        try
        {
            Thread.Sleep(500);
        }
        catch
        {
            // continue
        }

        // Kill anything still standing (several passes — main/guard may briefly race).
        var selfPid = Environment.ProcessId;
        for (var pass = 0; pass < 4; pass++)
        {
            Process[] agents;
            try
            {
                agents = Process.GetProcessesByName("EduGuardAgent");
            }
            catch
            {
                return;
            }

            var anyAlive = false;
            foreach (var process in agents)
            {
                try
                {
                    if (process.Id == selfPid)
                        continue;

                    anyAlive = true;
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort.
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (!anyAlive)
                break;

            try
            {
                Thread.Sleep(300);
            }
            catch
            {
                // continue
            }
        }

        AuditLog.Write("Uninstall: stopped the running Guardi process cluster.");
    }
}
