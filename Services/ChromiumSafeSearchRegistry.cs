using Microsoft.Win32;

namespace EduGuardAgent.Services;

internal sealed class ChromiumSafeSearchRegistry
{
    private static readonly RegistryPolicyTarget[] Targets =
    [
        new(@"SOFTWARE\Policies\Google\Chrome", "ForceGoogleSafeSearch", 1),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "ForceGoogleSafeSearch", 1),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "ForceBingSafeSearch", 2),
        new(@"SOFTWARE\Policies\BraveSoftware\Brave", "ForceGoogleSafeSearch", 1),
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

        errors.AddRange(RemoveLegacyYouTubeRestrictKeys());
        return errors;
    }

    public List<string> Restore(IReadOnlyList<RegistryValueBackup> backups)
    {
        var errors = new List<string>();

        foreach (var backup in backups)
        {
            if (string.Equals(backup.ValueName, "ForceYouTubeRestrict", StringComparison.OrdinalIgnoreCase))
                continue;
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

    private static List<string> RemoveLegacyYouTubeRestrictKeys()
    {
        var errors = new List<string>();
        foreach (var keyPath in new[]
        {
            @"SOFTWARE\Policies\Google\Chrome",
            @"SOFTWARE\Policies\Microsoft\Edge",
            @"SOFTWARE\Policies\BraveSoftware\Brave",
        })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
                key?.DeleteValue("ForceYouTubeRestrict", throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                errors.Add($"{keyPath}\\ForceYouTubeRestrict: {ex.Message}");
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
