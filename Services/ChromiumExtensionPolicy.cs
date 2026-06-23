using EduGuardAgent.Models;
using EduGuardAgent.Security;
using Microsoft.Win32;

namespace EduGuardAgent.Services;

/// <summary>
/// Force-installs the Guardi Image Shield extension in Chromium browsers and
/// pushes Dom-tunable settings through managed storage. Mirrors the registry
/// approach already used by <see cref="ChromiumSafeSearchRegistry"/>.
/// </summary>
internal sealed class ChromiumExtensionPolicy
{
    private static readonly string[] PolicyRoots =
    [
        @"SOFTWARE\Policies\Google\Chrome",
        @"SOFTWARE\Policies\Microsoft\Edge",
        @"SOFTWARE\Policies\BraveSoftware\Brave",
    ];

    public (List<RegistryStringBackup> Values, List<string> KeysCreated, List<string> Errors) Apply(
        string extensionId,
        string? updateUrl,
        ImageShieldSettings settings)
    {
        var values = new List<RegistryStringBackup>();
        var keysCreated = new List<string>();
        var errors = new List<string>();

        foreach (var root in PolicyRoots)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(updateUrl))
                {
                    ApplyInstallSources(root, updateUrl, values, keysCreated);
                    ApplyInstallAllowlist(root, extensionId, values, keysCreated);
                    ApplyForcelist(root, extensionId, updateUrl, values, keysCreated);
                }

                ApplyManagedSettings(root, extensionId, settings, values, keysCreated);
            }
            catch (Exception ex)
            {
                errors.Add($"{root}: {ex.Message}");
            }
        }

        return (values, keysCreated, errors);
    }

    private static void ApplyInstallSources(
        string root,
        string updateUrl,
        List<RegistryStringBackup> values,
        List<string> keysCreated)
    {
        if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri))
            return;

        var sourcePattern = $"{uri.Scheme}://{uri.Authority}/*";
        var sourcesPath = $@"{root}\ExtensionInstallSources";
        var existed = Registry.LocalMachine.OpenSubKey(sourcesPath) is not null;
        using var key = Registry.LocalMachine.CreateSubKey(sourcesPath, writable: true)
            ?? throw new InvalidOperationException("Could not open ExtensionInstallSources.");

        if (!existed)
            keysCreated.Add($"HKLM\\{sourcesPath}");

        var patterns = new List<string> { sourcePattern };
        if (updateUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !patterns.Contains("http://127.0.0.1/*", StringComparer.OrdinalIgnoreCase))
        {
            patterns.Add("http://127.0.0.1/*");
        }

        foreach (var pattern in patterns)
        {
            var exists = false;
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string s && string.Equals(s, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (exists)
                continue;

            var slot = NextFreeIntSlot(key);
            values.Add(new RegistryStringBackup
            {
                Hive = "HKLM",
                KeyPath = sourcesPath,
                ValueName = slot,
                HadValue = false,
                PreviousValue = null,
            });
            key.SetValue(slot, pattern, RegistryValueKind.String);
        }
    }

    private static void ApplyInstallAllowlist(
        string root,
        string extensionId,
        List<RegistryStringBackup> values,
        List<string> keysCreated)
    {
        var allowPath = $@"{root}\ExtensionInstallAllowlist";
        var existed = Registry.LocalMachine.OpenSubKey(allowPath) is not null;
        using var key = Registry.LocalMachine.CreateSubKey(allowPath, writable: true)
            ?? throw new InvalidOperationException("Could not open ExtensionInstallAllowlist.");

        if (!existed)
            keysCreated.Add($"HKLM\\{allowPath}");

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string s && string.Equals(s, extensionId, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var slot = NextFreeIntSlot(key);
        values.Add(new RegistryStringBackup
        {
            Hive = "HKLM",
            KeyPath = allowPath,
            ValueName = slot,
            HadValue = false,
            PreviousValue = null,
        });
        key.SetValue(slot, extensionId, RegistryValueKind.String);
    }

    private static void ApplyForcelist(
        string root,
        string extensionId,
        string updateUrl,
        List<RegistryStringBackup> values,
        List<string> keysCreated)
    {
        var forcelistPath = $@"{root}\ExtensionInstallForcelist";
        var existed = Registry.LocalMachine.OpenSubKey(forcelistPath) is not null;
        using var key = Registry.LocalMachine.CreateSubKey(forcelistPath, writable: true)
            ?? throw new InvalidOperationException("Could not open ExtensionInstallForcelist.");

        if (!existed)
            keysCreated.Add($"HKLM\\{forcelistPath}");

        var desired = $"{extensionId};{updateUrl}";

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string s || !s.StartsWith(extensionId + ";", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(s, desired, StringComparison.OrdinalIgnoreCase))
                return;

            var existing = key.GetValue(name);
            values.Add(new RegistryStringBackup
            {
                Hive = "HKLM",
                KeyPath = forcelistPath,
                ValueName = name,
                HadValue = existing is not null,
                PreviousValue = existing as string,
            });
            key.SetValue(name, desired, RegistryValueKind.String);
            AuditLog.Write($"Chromium policy: updated ExtensionInstallForcelist for {extensionId} -> {updateUrl}");
            return;
        }

        var slot = NextFreeIntSlot(key);
        values.Add(new RegistryStringBackup
        {
            Hive = "HKLM",
            KeyPath = forcelistPath,
            ValueName = slot,
            HadValue = false,
            PreviousValue = null,
        });
        key.SetValue(slot, desired, RegistryValueKind.String);
    }

    private static void ApplyManagedSettings(
        string root,
        string extensionId,
        ImageShieldSettings settings,
        List<RegistryStringBackup> values,
        List<string> keysCreated)
    {
        // Maps to chrome.storage.managed inside the extension.
        var policyPath = $@"{root}\3rdparty\extensions\{extensionId}\policy";
        var existed = Registry.LocalMachine.OpenSubKey(policyPath) is not null;
        using var key = Registry.LocalMachine.CreateSubKey(policyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open managed policy key.");

        if (!existed)
            keysCreated.Add($"HKLM\\{policyPath}");

        SetDword(key, policyPath, "shieldActive", settings.ShieldActive ? 1 : 0, values);
        SetDword(key, policyPath, "minSize", settings.MinSize, values);
        SetDword(key, policyPath, "maxPerSecond", settings.MaxPerSecond, values);
        // Floats are not representable as DWORD; the extension coerces strings.
        SetString(key, policyPath, "nsfwThreshold", settings.NsfwThreshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), values);
        SetString(key, policyPath, "thumbThreshold", settings.ThumbThreshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), values);
        SetString(key, policyPath, "sexyWeight", settings.SexyWeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), values);
        SetString(key, policyPath, "uiMode", settings.UiMode, values);
    }

    private static void SetDword(RegistryKey key, string keyPath, string name, int value, List<RegistryStringBackup> values)
    {
        var existing = key.GetValue(name);
        values.Add(new RegistryStringBackup
        {
            Hive = "HKLM",
            KeyPath = keyPath,
            ValueName = name,
            HadValue = existing is not null,
            PreviousValue = existing?.ToString(),
        });
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void SetString(RegistryKey key, string keyPath, string name, string value, List<RegistryStringBackup> values)
    {
        var existing = key.GetValue(name);
        values.Add(new RegistryStringBackup
        {
            Hive = "HKLM",
            KeyPath = keyPath,
            ValueName = name,
            HadValue = existing is not null,
            PreviousValue = existing as string,
        });
        key.SetValue(name, value, RegistryValueKind.String);
    }

    private static string NextFreeIntSlot(RegistryKey key)
    {
        var used = new HashSet<int>();
        foreach (var name in key.GetValueNames())
        {
            if (int.TryParse(name, out var n))
                used.Add(n);
        }

        var slot = 1;
        while (used.Contains(slot))
            slot++;
        return slot.ToString();
    }

    /// <summary>
    /// Updates managed-storage tuning + runtime active flag without touching force-install keys.
    /// </summary>
    public List<string> UpdateManagedSettings(string extensionId, ImageShieldSettings settings)
    {
        var errors = new List<string>();

        foreach (var root in PolicyRoots)
        {
            try
            {
                var policyPath = $@"{root}\3rdparty\extensions\{extensionId}\policy";
                using var key = Registry.LocalMachine.OpenSubKey(policyPath, writable: true);
                if (key is null)
                    continue;

                key.SetValue("shieldActive", settings.ShieldActive ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("minSize", settings.MinSize, RegistryValueKind.DWord);
                key.SetValue("maxPerSecond", settings.MaxPerSecond, RegistryValueKind.DWord);
                key.SetValue(
                    "nsfwThreshold",
                    settings.NsfwThreshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.String);
                key.SetValue(
                    "thumbThreshold",
                    settings.ThumbThreshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.String);
                key.SetValue(
                    "sexyWeight",
                    settings.SexyWeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.String);
                key.SetValue("uiMode", settings.UiMode, RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                errors.Add($"{root}: {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Removes force-install entries so Guardi can load the extension via --load-extension instead.
    /// </summary>
    public static bool StripForcelist(string extensionId)
    {
        var stripped = false;

        foreach (var root in PolicyRoots)
        {
            var forcelistPath = $@"{root}\ExtensionInstallForcelist";
            using var key = Registry.LocalMachine.OpenSubKey(forcelistPath, writable: true);
            if (key is null)
                continue;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string s
                    && s.StartsWith(extensionId + ";", StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                    stripped = true;
                }
            }
        }

        if (stripped)
            AuditLog.Write($"Chromium policy: removed ExtensionInstallForcelist for {extensionId} (unpacked mode).");

        return stripped;
    }

    /// <summary>
    /// Replaces a stale localhost forcelist left from a prior local-mode deploy.
    /// </summary>
    public static bool RepairStaleLocalhostForcelist(string extensionId, string webStoreUpdateUrl)
    {
        var desired = $"{extensionId};{webStoreUpdateUrl}";
        var repaired = false;

        foreach (var root in PolicyRoots)
        {
            var forcelistPath = $@"{root}\ExtensionInstallForcelist";
            using var key = Registry.LocalMachine.OpenSubKey(forcelistPath, writable: true);
            if (key is null)
                continue;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string s
                    || !s.StartsWith(extensionId + ";", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(s, desired, StringComparison.OrdinalIgnoreCase))
                    return repaired;

                if (!s.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    && !s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    return repaired;
                }

                key.SetValue(name, desired, RegistryValueKind.String);
                repaired = true;
            }
        }

        return repaired;
    }

    public static string DescribeForcelist(string extensionId)
    {
        var parts = new List<string>();
        foreach (var root in PolicyRoots)
        {
            var forcelistPath = $@"{root}\ExtensionInstallForcelist";
            using var key = Registry.LocalMachine.OpenSubKey(forcelistPath);
            if (key is null)
            {
                parts.Add($"{root}: (no forcelist key)");
                continue;
            }

            var match = key.GetValueNames()
                .Select(name => key.GetValue(name) as string)
                .FirstOrDefault(s => s is not null && s.StartsWith(extensionId + ";", StringComparison.OrdinalIgnoreCase));

            parts.Add(match is not null ? $"{root}: {match}" : $"{root}: (extension not in forcelist)");
        }

        return "Chromium policy forcelist — " + string.Join("; ", parts);
    }

    public static string DescribeInstallSources()
    {
        var parts = new List<string>();
        foreach (var root in PolicyRoots)
        {
            var sourcesPath = $@"{root}\ExtensionInstallSources";
            using var key = Registry.LocalMachine.OpenSubKey(sourcesPath);
            if (key is null)
            {
                parts.Add($"{root}: (no install sources key)");
                continue;
            }

            var values = key.GetValueNames()
                .Select(name => key.GetValue(name) as string)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            parts.Add(values.Count > 0
                ? $"{root}: {string.Join(", ", values)}"
                : $"{root}: (empty)");
        }

        return "Chromium policy install sources — " + string.Join("; ", parts);
    }

    public static bool HasStaleLocalhostInstallSources()
    {
        foreach (var root in PolicyRoots)
        {
            var sourcesPath = $@"{root}\ExtensionInstallSources";
            using var key = Registry.LocalMachine.OpenSubKey(sourcesPath);
            if (key is null)
                continue;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string s && IsStaleInstallSource(s))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Idempotent startup pass: drop dev-era localhost/file install sources and align forcelist
    /// with the active Chromium mode (Web Store vs unpacked sideload).
    /// </summary>
    public static IReadOnlyList<string> MigrateStartupPolicies(
        string extensionId,
        string? webStoreUpdateUrl,
        bool webStoreMode)
    {
        var actions = new List<string>();

        if (StripStaleDevInstallSources())
            actions.Add("removed stale localhost/file ExtensionInstallSources");

        if (webStoreMode && !string.IsNullOrWhiteSpace(webStoreUpdateUrl))
        {
            if (RepairStaleLocalhostForcelist(extensionId, webStoreUpdateUrl))
                actions.Add("repaired stale localhost forcelist → Chrome Web Store");
        }
        else if (!webStoreMode && StripForcelist(extensionId))
        {
            actions.Add("removed ExtensionInstallForcelist (unpacked dev mode)");
        }

        return actions;
    }

    public static bool TryMigrateStaleDevPolicies(string? extensionId = null, string? webStoreUpdateUrl = null)
    {
        extensionId ??= Config.ImageShieldExtensionId;
        if (string.IsNullOrWhiteSpace(extensionId)
            || extensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
        {
            return false;
        }

        var updateUrl = webStoreUpdateUrl ?? Config.ImageShieldChromeUpdateUrl;
        var webStoreMode = Config.ExtensionGuardEnforceChromium
            && !ChromiumUnpackedMode.IsActive
            && !string.IsNullOrWhiteSpace(updateUrl)
            && !updateUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

        var actions = MigrateStartupPolicies(extensionId, updateUrl, webStoreMode);
        if (actions.Count == 0)
            return false;

        AuditLog.Write($"Chromium policy migration — {string.Join("; ", actions)}.");
        if (webStoreMode)
            AuditLog.Write(DescribeForcelist(extensionId));

        return true;
    }

    private static bool StripStaleDevInstallSources()
    {
        var stripped = false;

        foreach (var root in PolicyRoots)
        {
            var sourcesPath = $@"{root}\ExtensionInstallSources";
            using var key = Registry.LocalMachine.OpenSubKey(sourcesPath, writable: true);
            if (key is null)
                continue;

            foreach (var name in key.GetValueNames().ToArray())
            {
                if (key.GetValue(name) is not string s || !IsStaleInstallSource(s))
                    continue;

                key.DeleteValue(name, throwOnMissingValue: false);
                stripped = true;
            }
        }

        return stripped;
    }

    private static bool IsStaleInstallSource(string pattern) =>
        pattern.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || pattern.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    public List<string> Restore(IReadOnlyList<RegistryStringBackup> values, IReadOnlyList<string> keysCreated)
    {
        var errors = new List<string>();

        foreach (var backup in values)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(backup.KeyPath, writable: true);
                if (key is null)
                    continue;

                if (backup.HadValue && backup.PreviousValue is not null)
                    key.SetValue(backup.ValueName, backup.PreviousValue);
                else
                    key.DeleteValue(backup.ValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                errors.Add($"{backup.KeyPath}\\{backup.ValueName}: {ex.Message}");
            }
        }

        // Remove keys we created, deepest first, only if now empty.
        foreach (var created in keysCreated.OrderByDescending(p => p.Length))
        {
            try
            {
                var path = created.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
                    ? created[5..]
                    : created;
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key is { ValueCount: 0, SubKeyCount: 0 })
                    Registry.LocalMachine.DeleteSubKey(path, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                errors.Add($"{created}: {ex.Message}");
            }
        }

        return errors;
    }
}

internal readonly record struct ImageShieldSettings(
    int MinSize,
    int MaxPerSecond,
    double NsfwThreshold,
    double ThumbThreshold,
    double SexyWeight,
    bool ShieldActive = true,
    string UiMode = AgentModeSlugs.Sub)
{
    public static ImageShieldSettings Default => new(80, 24, 0.45, 0.35, 1.0, true, AgentModeSlugs.Sub);

    public static ImageShieldSettings Inactive => new(80, 24, 0.45, 0.35, 1.0, false, AgentModeSlugs.Sub);

    public static ImageShieldSettings ForMode(string? modeSlug, bool active = true, ImageShieldSettings? tuning = null)
    {
        var baseSettings = tuning ?? Default;
        return baseSettings with
        {
            ShieldActive = active,
            UiMode = AgentModeSlugs.Normalize(modeSlug),
        };
    }
}
