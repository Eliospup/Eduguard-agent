using System.Runtime.Versioning;
using System.Security.Principal;

namespace EduGuardAgent.Security;

/// <summary>
/// Turns on the full SYSTEM-tier hardening (SYSTEM guardian + state lockdown) automatically
/// once supervision is active, so a distributed build needs no manual CLI step. Idempotent
/// and elevated-only; everything stays reversible via <see cref="SecurityTeardown"/> /
/// <c>--uninstall</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SecurityActivation
{
    public static void EnsureActive()
    {
        if (!IsElevated())
            return;

        try
        {
            // Supervision is (re)activating, so clear any stand-down marker — otherwise the
            // periodic re-assert below would reinstall the guardians only for them to see a stale
            // marker and immediately stand down again.
            SystemGuardian.ClearStandDown();

            if (!SystemGuardian.IsInstalled())
                SystemGuardian.TryInstall(out _);

            // Start it now so the pipe server is up and the secure folder gets hardened this
            // session, not only after the next reboot.
            SystemGuardian.StartIfNotRunning();

            // Safe Mode resilience: a real Windows Service registered under the SafeBoot keys, so
            // supervision resurrects even when booted into Safe Mode (where scheduled tasks don't
            // run). Additive and non-boot-critical — a failure here never blocks startup or boot.
            if (!BootGuardianService.IsInstalled())
                BootGuardianService.TryInstall(out _);
            BootGuardianService.StartIfNotRunning();

            ReconcileLockdown();
        }
        catch
        {
            // Best-effort; never block supervision startup.
        }
    }

    /// <summary>
    /// Enables state lockdown (SYSTEM-only secure folder) ONLY once the SYSTEM guardian is
    /// actually serving its pipe — otherwise the elevated agent could no longer write secure
    /// state and no guardian could write on its behalf, silently bricking every security-state
    /// write (mode, PIN, the process-killer flag, the shield runtime flag, …). If lockdown is
    /// already on but the guardian has stopped responding (broken install, dev build, killed
    /// task), relax it back to the Stage-1 admins-writable ACL so persistence keeps working.
    /// Stage-1 still encrypts + tamper-checks state and keeps the non-admin supervised user out.
    /// </summary>
    private static void ReconcileLockdown()
    {
        var guardianServing = WaitForGuardianPipe();

        if (guardianServing)
        {
            if (!SecureLockdown.IsEnabled())
                SecureLockdown.Enable();
            return;
        }

        if (SecureLockdown.IsEnabled())
        {
            AuditLog.Write(
                "SECURITY: SYSTEM guardian pipe not responding — relaxing state lockdown to admins-writable "
                + "so secure-state persistence keeps working (Stage-1 encryption + tamper-check still apply).");
            SecureLockdown.Disable();
        }
    }

    /// <summary>
    /// The guardian task is started asynchronously (schtasks /Run), so its pipe server needs a
    /// moment to come up. Poll a real ping round-trip a few times before concluding it is down.
    /// </summary>
    private static bool WaitForGuardianPipe()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (SecureStateIpcClient.IsGuardianResponding())
                return true;

            Thread.Sleep(500);
        }

        return false;
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
