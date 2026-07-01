using System.Runtime.Versioning;
using EduGuardAgent.Services;

namespace EduGuardAgent.Security;

/// <summary>
/// Fully reverses everything the security hardening installs, so removing Guardi leaves
/// nothing locked behind: the SYSTEM guardian task, the user auto-start task, and the
/// hardened ProgramData state folder (ownership reset + deleted). Safe to run repeatedly.
///
/// Intended to be invoked elevated, e.g. <c>EduGuardAgent.exe --uninstall</c> from an
/// uninstaller or a manual cleanup.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SecurityTeardown
{
    public static void RunAll()
    {
        AuditLog.Write("Security teardown requested.");

        try
        {
            // Drop lockdown first so the secure folder is reclaimable, then remove the task.
            SecureLockdown.Disable();
        }
        catch
        {
            // continue
        }

        try
        {
            SystemGuardian.TryUninstall(out _);
        }
        catch
        {
            // continue
        }

        try
        {
            WindowsAutoStartService.TryDisable(out _);
        }
        catch
        {
            // continue
        }

        try
        {
            SecureDataPaths.Cleanup();
        }
        catch
        {
            // continue
        }

        AuditLog.Write("Security teardown complete.");
    }
}
