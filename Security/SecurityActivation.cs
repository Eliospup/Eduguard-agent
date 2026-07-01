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
            if (!SystemGuardian.IsInstalled())
                SystemGuardian.TryInstall(out _);

            // Start it now so the pipe server is up and the secure folder gets hardened this
            // session, not only after the next reboot.
            SystemGuardian.StartIfNotRunning();

            if (!SecureLockdown.IsEnabled())
                SecureLockdown.Enable();
        }
        catch
        {
            // Best-effort; never block supervision startup.
        }
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
