using System.Runtime.Versioning;
using EduGuardAgent.Services;

namespace EduGuardAgent.Security;

/// <summary>
/// Fully reverses everything Guardi installs, so removing it leaves nothing behind: the
/// machine-wide footprint (hosts, DNS, browser policies, native messaging, root certificate),
/// the SYSTEM guardian task, the user auto-start task, the hardened ProgramData state folder,
/// and every per-user data folder across all Windows profiles. Safe to run repeatedly.
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

        // Drop lockdown first so the secure folder (and the backups the footprint teardown
        // needs to read) become reclaimable, then stop the guardian so it can't re-harden.
        try
        {
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
            BootGuardianService.TryUninstall(out _);
        }
        catch
        {
            // continue
        }

        // Revert the machine-wide footprint (hosts, DNS, browser policies, native messaging,
        // root certificate). Runs BEFORE the data folders are deleted — several reverts read
        // their original state from a persisted backup under ProgramData\EduGuard.
        try
        {
            SystemFootprintTeardown.RevertAll();
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

        // Relax + delete the hardened secure sub-folder, then remove all data folders.
        try
        {
            SecureDataPaths.Cleanup();
        }
        catch
        {
            // continue
        }

        AuditLog.Write("Security teardown complete — deleting data folders.");

        try
        {
            DeleteAllDataFolders();
        }
        catch
        {
            // continue
        }
    }

    /// <summary>
    /// Deletes the shared ProgramData state folder and the per-user AppData state folder for
    /// EVERY Windows profile, so no trace of Guardi's data is left on the machine.
    /// </summary>
    private static void DeleteAllDataFolders()
    {
        // Shared machine-wide state: C:\ProgramData\EduGuard (secure\, native\, certs\, backups…).
        TryDeleteDir(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Config.AgentDataDir));

        // Per-user state: C:\Users\<name>\AppData\Roaming\EduGuard for all profiles.
        try
        {
            var currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var usersRoot = Directory.GetParent(currentUserProfile)?.FullName;
            if (usersRoot is null || !Directory.Exists(usersRoot))
                return;

            foreach (var profile in Directory.EnumerateDirectories(usersRoot))
            {
                TryDeleteDir(Path.Combine(profile, "AppData", "Roaming", Config.AgentDataDir));
                // Legacy/local fallback location, just in case.
                TryDeleteDir(Path.Combine(profile, "AppData", "Local", Config.AgentDataDir));
            }
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Per-profile data cleanup failed: {ex.Message}");
        }
    }

    private static void TryDeleteDir(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        // First pass: straight delete.
        try
        {
            Directory.Delete(dir, recursive: true);
            return;
        }
        catch
        {
            // Likely ACL or lock — try harder below.
        }

        // Second pass: reset attributes + ACLs, then retry.
        try
        {
            ForceResetAttributes(dir);
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"TryDeleteDir failed even after attribute reset ({dir}): {ex.Message}");
        }
    }

    private static void ForceResetAttributes(string dir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }

            foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
            {
                try { new DirectoryInfo(sub).Attributes = FileAttributes.Directory; } catch { }
            }
        }
        catch
        {
            // Best-effort enumeration.
        }
    }
}
