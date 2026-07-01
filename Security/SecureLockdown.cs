using System.Runtime.Versioning;
using System.Security.Principal;

namespace EduGuardAgent.Security;

/// <summary>
/// Stage 3 switch. When lockdown is enabled the secure-state folder is hardened to
/// SYSTEM-only (even administrators lose write), so the elevated user agent can no longer
/// edit mode/PIN/flags directly — it must route writes to the SYSTEM guardian over an
/// authenticated pipe. An admin would have to escalate to SYSTEM (or impersonate the signed
/// agent over the pipe) to tamper, instead of just editing a file.
///
/// The flag lives in the admins-writable parent folder (not the locked secure subfolder),
/// so it can always be toggled with <c>--enable-lockdown</c> / <c>--disable-lockdown</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SecureLockdown
{
    /// <summary>Named pipe the guardian exposes for SYSTEM-side state writes.</summary>
    public const string PipeName = "EduGuard.SecureState.v1";

    private static readonly string ParentDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir);

    private static readonly string FlagPath = Path.Combine(ParentDir, "lockdown.flag");

    public static bool IsEnabled()
    {
        try
        {
            return File.Exists(FlagPath);
        }
        catch
        {
            return false;
        }
    }

    public static bool Enable()
    {
        try
        {
            Directory.CreateDirectory(ParentDir);
            File.WriteAllText(FlagPath, DateTimeOffset.UtcNow.ToString("o"));
            AuditLog.Write("SECURITY: state lockdown enabled (SYSTEM-only writes).");
            return true;
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Lockdown enable failed: {ex.Message}");
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            if (File.Exists(FlagPath))
                File.Delete(FlagPath);

            // Relax the secure folder back to admins-writable so an offline machine (guardian
            // not running) isn't left locked.
            SecureDataPaths.RelaxToAdminWritable();
            AuditLog.Write("SECURITY: state lockdown disabled.");
            return true;
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Lockdown disable failed: {ex.Message}");
            return false;
        }
    }

    public static bool IsSystem()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User is { } sid
                && sid.IsWellKnown(WellKnownSidType.LocalSystemSid);
        }
        catch
        {
            return false;
        }
    }
}
