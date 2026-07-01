using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EduGuardAgent.Security;

/// <summary>
/// Resolves where security-critical state lives and keeps it out of the supervised user's
/// reach. When the agent runs elevated, state goes under <c>C:\ProgramData\EduGuard\secure</c>
/// with a hardened DACL: SYSTEM and Administrators get full control, but the Users group is
/// read-only. A standard (non-admin) supervised user therefore cannot edit or delete the
/// mode, the security flags, or the PIN verifier at all.
///
/// Honest limit: an administrator is in the Administrators group, so this does not lock an
/// admin out by itself — but the folder is owned by Administrators (not the user), so an
/// admin must deliberately take ownership / run elevated tooling, which the agent detects as
/// drift and re-asserts + audits. Fully locking out an admin requires the state to be owned
/// by a SYSTEM service (the next stage).
///
/// Falls back to the per-user AppData location when not elevated, so a non-elevated run
/// never crashes trying to write into a folder it cannot modify.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SecureDataPaths
{
    private static readonly string ProgramDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "secure");

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir);

    private static readonly object Gate = new();
    private static string? _resolvedDir;

    /// <summary>The active protected directory (hardened ProgramData when elevated, else AppData).</summary>
    public static string Dir
    {
        get
        {
            if (_resolvedDir is not null)
                return _resolvedDir;

            lock (Gate)
                return _resolvedDir ??= Resolve();
        }
    }

    /// <summary>Path for a state file, migrating any legacy copy from AppData on first use.</summary>
    public static string PathFor(string fileName)
    {
        var dir = Dir;
        var target = Path.Combine(dir, fileName);

        if (!string.Equals(dir, AppDataDir, StringComparison.OrdinalIgnoreCase))
            MigrateLegacy(fileName, target);

        return target;
    }

    /// <summary>
    /// Fully reverses the hardening so nothing is left locked after Guardi is removed:
    /// takes ownership, restores a normal inheritable DACL, then deletes the protected
    /// folder. Safe to call repeatedly and when the folder never existed.
    /// </summary>
    public static void Cleanup()
    {
        // Reset the cached resolution so a later run re-resolves cleanly.
        lock (Gate)
            _resolvedDir = null;

        if (!Directory.Exists(ProgramDataDir))
            return;

        try
        {
            RelaxAcl(ProgramDataDir);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Secure state ACL relax failed during cleanup: {ex.Message}");
        }

        try
        {
            Directory.Delete(ProgramDataDir, recursive: true);
            AuditLog.Write("Protected secure-state folder removed.");
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Secure state folder delete failed during cleanup: {ex.Message}");
        }
    }

    private static void RelaxAcl(string dir)
    {
        TakeOwnershipAsAdmins(dir);

        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        security.AddAccessRule(new FileSystemAccessRule(
            admins,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(security);
    }

    /// <summary>Reverts a SYSTEM-only (lockdown) folder back to the admins-writable Stage-1 ACL.</summary>
    public static void RelaxToAdminWritable()
    {
        if (!Directory.Exists(ProgramDataDir))
            return;

        try
        {
            TakeOwnershipAsAdmins(ProgramDataDir);
            ApplyStandardAcl(ProgramDataDir);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Secure state relax-to-admin failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes ownership as Administrators, enabling the take-ownership/restore privileges so an
    /// elevated admin can reclaim a folder the SYSTEM guardian owns. Keeps lockdown reversible.
    /// </summary>
    private static void TakeOwnershipAsAdmins(string dir)
    {
        TokenPrivilege.TryEnable("SeTakeOwnershipPrivilege");
        TokenPrivilege.TryEnable("SeRestorePrivilege");

        try
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var di = new DirectoryInfo(dir);
            var owning = di.GetAccessControl();
            owning.SetOwner(admins);
            di.SetAccessControl(owning);
        }
        catch
        {
            // Best-effort; subsequent DACL/delete may still succeed.
        }
    }

    /// <summary>Re-applies the hardened DACL if it has drifted (e.g. an admin reset it).</summary>
    public static void ReassertAcl()
    {
        if (_resolvedDir is null || string.Equals(_resolvedDir, AppDataDir, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            HardenAcl(_resolvedDir);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static string Resolve()
    {
        // Only use the hardened ProgramData location when we can actually write there.
        if (!IsElevated())
        {
            Directory.CreateDirectory(AppDataDir);
            return AppDataDir;
        }

        try
        {
            Directory.CreateDirectory(ProgramDataDir);
            try
            {
                HardenAcl(ProgramDataDir);
            }
            catch (Exception ex)
            {
                AuditLog.Write($"Secure state ACL could not be applied: {ex.Message}");
            }

            return ProgramDataDir;
        }
        catch
        {
            Directory.CreateDirectory(AppDataDir);
            return AppDataDir;
        }
    }

    private static void MigrateLegacy(string fileName, string target)
    {
        try
        {
            if (File.Exists(target))
                return;

            var legacy = Path.Combine(AppDataDir, fileName);
            if (!File.Exists(legacy))
                return;

            File.Copy(legacy, target, overwrite: false);
            AuditLog.Write($"Migrated {fileName} into protected storage.");
            try
            {
                File.Delete(legacy);
            }
            catch
            {
                // Leaving the old copy behind is harmless; the protected one is authoritative.
            }
        }
        catch
        {
            // Best-effort; the store will simply create a fresh file in the protected dir.
        }
    }

    private static void HardenAcl(string dir)
    {
        // Under lockdown only the SYSTEM guardian owns the ACL; the elevated agent must not
        // fight it (and can't set a SYSTEM owner anyway).
        if (SecureLockdown.IsEnabled() && !SecureLockdown.IsSystem())
            return;

        if (SecureLockdown.IsEnabled())
            ApplySystemOnlyAcl(dir);
        else
            ApplyStandardAcl(dir);
    }

    /// <summary>Stage 1: owner Administrators; SYSTEM+Admins full, Users read-only.</summary>
    private static void ApplyStandardAcl(string dir)
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetOwner(admins);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        new DirectoryInfo(dir).SetAccessControl(security);
    }

    /// <summary>Stage 3 lockdown: owner SYSTEM; SYSTEM full, even Administrators read-only.</summary>
    private static void ApplySystemOnlyAcl(string dir)
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetOwner(system);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            admins, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));

        new DirectoryInfo(dir).SetAccessControl(security);
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
