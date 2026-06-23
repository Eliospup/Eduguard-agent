using Microsoft.Win32;

namespace EduGuardAgent.Services;

internal sealed class ChromiumYouTubeRestrictRegistry
{
    // ForceYouTubeRestrict: 0 = off, 1 = moderate, 2 = strict
    private const int StrictRestrict = 2;

    private static readonly RegistryPolicyTarget[] Targets =
    [
        new(@"SOFTWARE\Policies\Google\Chrome", "ForceYouTubeRestrict", StrictRestrict),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "ForceYouTubeRestrict", StrictRestrict),
        new(@"SOFTWARE\Policies\BraveSoftware\Brave", "ForceYouTubeRestrict", StrictRestrict),
    ];

    public (List<RegistryValueBackup> Backups, List<string> Errors) Apply()
    {
        var backups = new List<RegistryValueBackup>();
        var errors = new List<string>();

        foreach (var target in Targets)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(target.KeyPath, writable: true)
                    ?? Registry.LocalMachine.CreateSubKey(target.KeyPath, writable: true);

                if (key is null)
                {
                    errors.Add($"Could not open registry key: {target.KeyPath}");
                    continue;
                }

                var existing = key.GetValue(target.ValueName);
                backups.Add(new RegistryValueBackup
                {
                    KeyPath = target.KeyPath,
                    ValueName = target.ValueName,
                    HadValue = existing is not null,
                    PreviousDword = existing is int dword ? dword : null,
                });

                key.SetValue(target.ValueName, target.DesiredValue, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                errors.Add($"{target.KeyPath}\\{target.ValueName}: {ex.Message}");
            }
        }

        return (backups, errors);
    }

    public List<string> Restore(IReadOnlyList<RegistryValueBackup> backups)
    {
        var errors = new List<string>();

        foreach (var backup in backups)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(backup.KeyPath, writable: true);
                if (key is null)
                    continue;

                if (backup.HadValue && backup.PreviousDword.HasValue)
                    key.SetValue(backup.ValueName, backup.PreviousDword.Value, RegistryValueKind.DWord);
                else
                    key.DeleteValue(backup.ValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                errors.Add($"{backup.KeyPath}\\{backup.ValueName}: {ex.Message}");
            }
        }

        return errors;
    }

    public List<string> ForceRemove()
    {
        var errors = new List<string>();

        foreach (var target in Targets)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(target.KeyPath, writable: true);
                if (key is null)
                    continue;

                key.DeleteValue(target.ValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                errors.Add($"{target.KeyPath}\\{target.ValueName}: {ex.Message}");
            }
        }

        return errors;
    }

    public bool HasActivePolicies()
    {
        foreach (var target in Targets)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(target.KeyPath);
                if (key?.GetValue(target.ValueName) is int value && value == target.DesiredValue)
                    return true;
            }
            catch
            {
                // ignored
            }
        }

        return false;
    }

    private readonly record struct RegistryPolicyTarget(string KeyPath, string ValueName, int DesiredValue);
}
