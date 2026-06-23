using System.Text.Json.Nodes;

namespace EduGuardAgent.Services;

/// <summary>
/// Firefox enterprise policy install — signed XPI, local unsigned, or Developer Edition.
/// </summary>
internal sealed class FirefoxExtensionPolicy
{
    private const string PolicyFileName = "policies.json";

    public (List<string> PolicyPaths, List<string> Errors) ApplyPolicies(
        string addonId,
        string installUrl,
        string? xpiSourcePath = null,
        bool allowUnsigned = false)
    {
        var policyPaths = new List<string>();
        var errors = new List<string>();

        foreach (var installRoot in FindInstallRoots())
        {
            var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);
            try
            {
                var root = ReadOrCreate(policyPath);
                var policies = EnsureObject(root, "policies");
                var extensionSettings = EnsureObject(policies, "ExtensionSettings");

                extensionSettings[addonId] = new JsonObject
                {
                    ["installation_mode"] = "force_installed",
                    ["install_url"] = installUrl,
                };

                if (allowUnsigned)
                {
                    var preferences = EnsureObject(policies, "Preferences");
                    preferences["xpinstall.signatures.required"] = new JsonObject
                    {
                        ["Value"] = false,
                        ["Status"] = "locked",
                    };
                }

                Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
                FirefoxInstallRoots.WriteIndented(policyPath, root);
                policyPaths.Add(policyPath);
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        if (policyPaths.Count == 0)
            errors.Add("Firefox install folder not found on this PC.");

        return (policyPaths, errors);
    }

    /// <summary>
    /// Points ExtensionSettings at the XPI Guardi already copied under distribution/extensions.
    /// Avoids Firefox fetching install_url over the network (broken when a system proxy is dead).
    /// </summary>
    public (List<string> PolicyPaths, List<string> Errors) ApplyBundledSignedPolicies(
        string addonId,
        ImageShieldSettings settings)
    {
        var policyPaths = new List<string>();
        var errors = new List<string>();

        foreach (var installRoot in FindInstallRoots())
        {
            var bundledXpi = Path.Combine(installRoot, "distribution", "extensions", addonId + ".xpi");
            if (!File.Exists(bundledXpi))
            {
                errors.Add($"{installRoot}: bundled XPI missing at {bundledXpi}");
                continue;
            }

            var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);
            try
            {
                var root = ReadOrCreate(policyPath);
                var policies = EnsureObject(root, "policies");
                var extensionSettings = EnsureObject(policies, "ExtensionSettings");
                var installUrl = ToFileInstallUrl(bundledXpi);

                extensionSettings[addonId] = new JsonObject
                {
                    ["installation_mode"] = "force_installed",
                    ["install_url"] = installUrl,
                };

                var extensions = EnsureObject(policies, "Extensions");
                AddUniqueToArray(extensions, "Locked", addonId);

                ApplyThirdPartySettings(policies, addonId, settings);

                Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
                FirefoxInstallRoots.WriteIndented(policyPath, root);
                policyPaths.Add(policyPath);
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        if (policyPaths.Count == 0)
            errors.Add("Firefox bundled signed policy not applied — no distribution XPI found.");

        return (policyPaths, errors);
    }

    internal static string ToFileInstallUrl(string xpiPath)
    {
        var full = Path.GetFullPath(xpiPath);
        return new Uri(full).AbsoluteUri;
    }

    /// <summary>
    /// Installs unsigned MV2 on Firefox Developer Edition via unpacked sideload only.
    /// Unsigned local XPIs fail policy install with ERROR_CORRUPT_FILE — Mozilla documents
    /// distribution/extensions/{id}/ (unpacked) for distro bundles on Dev Edition.
    /// Do not use ExtensionSettings file:/// or Extensions.Install with unsigned XPI.
    /// </summary>
    public (List<string> PolicyPaths, List<string> BundledArtifactPaths, bool ExtensionFilesChanged, bool PoliciesChanged, List<string> Errors) ApplyLocalUnsigned(
        string addonId,
        string unpackedSourceDir,
        ImageShieldSettings settings)
    {
        var policyPaths = new List<string>();
        var bundledPaths = new List<string>();
        var errors = new List<string>();
        var extensionFilesChanged = false;
        var policiesChanged = false;

        if (!Directory.Exists(unpackedSourceDir)
            || !File.Exists(Path.Combine(unpackedSourceDir, "manifest.json")))
        {
            errors.Add($"Firefox unpacked source missing at {unpackedSourceDir}");
            return (policyPaths, bundledPaths, extensionFilesChanged, policiesChanged, errors);
        }

        errors.AddRange(ValidateDeployedManifest(Path.Combine(unpackedSourceDir, "manifest.json"), "build"));
        if (errors.Any(e => e.Contains("must be manifest_version 2", StringComparison.OrdinalIgnoreCase)
            || e.Contains("missing gecko add-on ID", StringComparison.OrdinalIgnoreCase)
            || e.Contains("PNG icon", StringComparison.OrdinalIgnoreCase)))
        {
            return (policyPaths, bundledPaths, extensionFilesChanged, policiesChanged, errors);
        }

        var installRoots = FirefoxEditionHelper.FindDeveloperEditionInstallRoots().ToList();
        if (installRoots.Count == 0)
        {
            errors.Add(
                "Firefox Developer Edition not found. Firefox Release cannot load unsigned extensions — " +
                "install Firefox Developer Edition or sign the XPI on addons.mozilla.org.");
            return (policyPaths, bundledPaths, extensionFilesChanged, policiesChanged, errors);
        }

        foreach (var installRoot in installRoots)
        {
            try
            {
                var distExtDir = Path.Combine(installRoot, "distribution", "extensions");
                Directory.CreateDirectory(distExtDir);

                var distUnpacked = Path.Combine(distExtDir, addonId);

                var distXpi = Path.Combine(distExtDir, addonId + ".xpi");
                if (File.Exists(distXpi))
                    File.Delete(distXpi);

                var canonicalXpi = FirefoxLocalPaths.CanonicalXpiPath(addonId);
                if (File.Exists(canonicalXpi))
                    File.Delete(canonicalXpi);

                if (!ExtensionBundleSync.SyncIfChanged(unpackedSourceDir, distUnpacked, out var bundleChanged))
                    errors.Add($"{installRoot}: failed to sync unpacked extension bundle.");
                else if (bundleChanged)
                    extensionFilesChanged = true;

                bundledPaths.Add(distUnpacked);

                FirefoxDistributionAutoconfig.Deploy(installRoot);

                var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);
                var root = ReadOrCreate(policyPath);
                var policies = EnsureObject(root, "policies");

                if (policies["ExtensionSettings"] is JsonObject extensionSettings)
                {
                    extensionSettings.Remove(addonId);
                    if (extensionSettings.Count == 0)
                        policies.Remove("ExtensionSettings");
                }

                var extensions = EnsureObject(policies, "Extensions");
                RemoveFromArray(extensions, "Install", value =>
                    value.Contains(addonId, StringComparison.OrdinalIgnoreCase)
                    || value.Contains("EduGuard", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("guardi-image-shield", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("image-shield", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("ProgramData", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("//", StringComparison.OrdinalIgnoreCase));
                AddUniqueToArray(extensions, "Locked", addonId);

                var preferences = EnsureObject(policies, "Preferences");
                preferences["xpinstall.signatures.required"] = new JsonObject
                {
                    ["Value"] = false,
                    ["Status"] = "locked",
                };
                preferences["extensions.autoDisableScopes"] = new JsonObject
                {
                    ["Value"] = 0,
                    ["Status"] = "locked",
                };
                preferences["extensions.enabledScopes"] = new JsonObject
                {
                    ["Value"] = 15,
                    ["Status"] = "locked",
                };
                preferences["extensions.installDistroAddons"] = new JsonObject
                {
                    ["Value"] = true,
                    ["Status"] = "locked",
                };
                preferences["browser.newtabpage.activity-stream.enabled"] = new JsonObject
                {
                    ["Value"] = false,
                    ["Status"] = "locked",
                };

                ApplyThirdPartySettings(policies, addonId, settings);

                if (TryWritePoliciesIfChanged(policyPath, root, out var policyChanged) && policyChanged)
                    policiesChanged = true;

                policyPaths.Add(policyPath);

                errors.AddRange(ValidateDeployedManifest(Path.Combine(distUnpacked, "manifest.json"), installRoot));
            }
            catch (Exception ex)
            {
                errors.Add($"{installRoot}: {ex.Message}");
            }
        }

        if (policyPaths.Count == 0 && errors.Count == 0)
            errors.Add("Firefox Developer Edition install folder not found on this PC.");

        return (policyPaths, bundledPaths, extensionFilesChanged, policiesChanged, errors);
    }

    public (List<string> BundledXpiPaths, List<string> Errors) DeployBundledOnly(
        string addonId,
        string xpiSourcePath)
    {
        var bundledPaths = new List<string>();
        var errors = new List<string>();

        foreach (var installRoot in FindInstallRoots())
        {
            try
            {
                var destDir = Path.Combine(installRoot, "distribution", "extensions");
                Directory.CreateDirectory(destDir);
                var destPath = Path.Combine(destDir, addonId + ".xpi");
                File.Copy(xpiSourcePath, destPath, overwrite: true);
                bundledPaths.Add(destPath);
            }
            catch (Exception ex)
            {
                errors.Add($"{installRoot}\\distribution\\extensions: {ex.Message}");
            }
        }

        return (bundledPaths, errors);
    }

    public List<string> Restore(string addonId)
    {
        var errors = new List<string>();

        foreach (var installRoot in FindInstallRoots())
        {
            FirefoxDistributionAutoconfig.Remove(installRoot);

            var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);
            if (!File.Exists(policyPath))
                continue;

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(policyPath)) as JsonObject;
                if (root?["policies"] is not JsonObject policies)
                    continue;

                if (policies["ExtensionSettings"] is JsonObject extensionSettings)
                {
                    if (extensionSettings.Remove(addonId) && extensionSettings.Count == 0)
                        policies.Remove("ExtensionSettings");
                }

                if (policies["Extensions"] is JsonObject extensions)
                {
                    RemoveFromArray(extensions, "Locked", addonId);
                    RemoveFromArray(extensions, "Install", value =>
                        value.Contains(addonId, StringComparison.OrdinalIgnoreCase)
                        || value.Contains("EduGuard", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("guardi-image-shield", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("image-shield", StringComparison.OrdinalIgnoreCase)
                        || value.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
                        || value.StartsWith("//", StringComparison.OrdinalIgnoreCase));

                    if (extensions.Count == 0)
                        policies.Remove("Extensions");
                }

                if (policies["3rdparty"] is JsonObject thirdParty
                    && thirdParty["Extensions"] is JsonObject thirdPartyExtensions
                    && thirdPartyExtensions.Remove(addonId))
                {
                    if (thirdPartyExtensions.Count == 0)
                        thirdParty.Remove("Extensions");
                    if (thirdParty.Count == 0)
                        policies.Remove("3rdparty");
                }

                if (policies["Preferences"] is JsonObject preferences)
                {
                    preferences.Remove("xpinstall.signatures.required");
                    preferences.Remove("extensions.autoDisableScopes");
                    preferences.Remove("extensions.enabledScopes");
                    preferences.Remove("extensions.installDistroAddons");
                    preferences.Remove("browser.newtabpage.enabled");
                    preferences.Remove("browser.newtabpage.activity-stream.enabled");
                    preferences.Remove("browser.newtab.url");
                    preferences.Remove("browser.startup.homepage");
                    preferences.Remove("browser.startup.page");
                    if (preferences.Count == 0)
                        policies.Remove("Preferences");
                }

                FirefoxInstallRoots.WriteIndented(policyPath, root);
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        return errors;
    }

    public static List<string> RestoreBundledXpi(string bundledPath) =>
        FirefoxProfileExtensionInstaller.RemoveDeployed(bundledPath);

    /// <summary>
    /// Removes Guardi extension policies mistakenly written to Firefox Release.
    /// Release ignores signature overrides and cannot load unsigned XPIs.
    /// </summary>
    public List<string> CleanupReleaseInstallRoots(string addonId)
    {
        var errors = new List<string>();

        foreach (var installRoot in FindInstallRoots().Where(root =>
                     !FirefoxEditionHelper.IsDeveloperEditionPath(root)))
        {
            var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);
            if (!File.Exists(policyPath))
                continue;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(policyPath)) is not JsonObject root)
                    continue;

                if (root["policies"] is not JsonObject policies)
                    continue;

                var changed = false;

                if (policies["ExtensionSettings"] is JsonObject extensionSettings
                    && extensionSettings.Remove(addonId))
                {
                    changed = true;
                    if (extensionSettings.Count == 0)
                        policies.Remove("ExtensionSettings");
                }

                if (policies["Extensions"] is JsonObject extensions)
                {
                    RemoveFromArray(extensions, "Locked", addonId);
                    RemoveFromArray(extensions, "Install", value =>
                        value.Contains(addonId, StringComparison.OrdinalIgnoreCase)
                        || value.Contains("EduGuard", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("guardi-image-shield", StringComparison.OrdinalIgnoreCase));

                    if (extensions.Count == 0)
                    {
                        policies.Remove("Extensions");
                        changed = true;
                    }
                    else
                    {
                        changed = true;
                    }
                }

                if (policies["Preferences"] is JsonObject preferences
                    && preferences.Remove("xpinstall.signatures.required"))
                {
                    changed = true;
                    if (preferences.Count == 0)
                        policies.Remove("Preferences");
                }

                var distExtDir = Path.Combine(installRoot, "distribution", "extensions");
                var distXpi = Path.Combine(distExtDir, addonId + ".xpi");
                if (File.Exists(distXpi))
                {
                    File.Delete(distXpi);
                    changed = true;
                }

                var distUnpacked = Path.Combine(distExtDir, addonId);
                if (Directory.Exists(distUnpacked))
                {
                    Directory.Delete(distUnpacked, recursive: true);
                    changed = true;
                }

                if (!changed)
                    continue;

                FirefoxInstallRoots.WriteIndented(policyPath, root);
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        return errors;
    }

    private static JsonObject ReadOrCreate(string policyPath) =>
        FirefoxInstallRoots.ReadOrCreate(policyPath);

    private static JsonObject EnsureObject(JsonObject parent, string name) =>
        FirefoxInstallRoots.EnsureObject(parent, name);

    private static void AddUniqueToArray(JsonObject parent, string name, string value)
    {
        var array = parent[name] as JsonArray ?? new JsonArray();
        foreach (var item in array)
        {
            if (string.Equals(item?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase))
            {
                parent[name] = array;
                return;
            }
        }

        array.Add(value);
        parent[name] = array;
    }

    private static void RemoveFromArray(JsonObject parent, string name, string value)
    {
        if (parent[name] is not JsonArray array)
            return;

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (string.Equals(array[i]?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase))
                array.RemoveAt(i);
        }

        if (array.Count == 0)
            parent.Remove(name);
    }

    private static void RemoveFromArray(JsonObject parent, string name, Func<string, bool> shouldRemove)
    {
        if (parent[name] is not JsonArray array)
            return;

        for (var i = array.Count - 1; i >= 0; i--)
        {
            var text = array[i]?.GetValue<string>();
            if (text is not null && shouldRemove(text))
                array.RemoveAt(i);
        }

        if (array.Count == 0)
            parent.Remove(name);
    }

    /// <summary>
    /// Updates extension managed storage (3rdparty) without touching install artifacts.
    /// </summary>
    public (List<string> Errors, bool Changed) UpdateRuntimeSettings(string addonId, ImageShieldSettings settings)
    {
        var errors = new List<string>();
        var changed = false;
        var roots = FirefoxEditionHelper.UseSignedReleaseTarget
            ? FirefoxEditionHelper.FindReleaseInstallRoots().ToList()
            : FirefoxEditionHelper.FindDeveloperEditionInstallRoots().ToList();
        if (roots.Count == 0)
            roots = FindInstallRoots().ToList();

        foreach (var installRoot in roots)
        {
            var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);

            try
            {
                JsonObject root;
                if (File.Exists(policyPath))
                {
                    root = JsonNode.Parse(File.ReadAllText(policyPath)) as JsonObject
                        ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                var policies = EnsureObject(root, "policies");
                ApplyThirdPartySettings(policies, addonId, settings);
                if (TryWritePoliciesIfChanged(policyPath, root, out var policyChanged) && policyChanged)
                    changed = true;
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        return (errors, changed);
    }

    /// <summary>
    /// Updates ExtensionSettings install_url when store-config bumps the XPI release URL.
    /// </summary>
    public (List<string> Errors, bool Changed) UpdateInstallUrl(string addonId, string installUrl)
    {
        var errors = new List<string>();
        var changed = false;
        var roots = FirefoxEditionHelper.UseSignedReleaseTarget
            ? FirefoxEditionHelper.FindReleaseInstallRoots().ToList()
            : FirefoxEditionHelper.FindDeveloperEditionInstallRoots().ToList();
        if (roots.Count == 0)
            roots = FindInstallRoots().ToList();

        foreach (var installRoot in roots)
        {
            var policyPath = Path.Combine(installRoot, "distribution", PolicyFileName);

            try
            {
                JsonObject root;
                if (File.Exists(policyPath))
                {
                    root = JsonNode.Parse(File.ReadAllText(policyPath)) as JsonObject
                        ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                var policies = EnsureObject(root, "policies");
                var extensionSettings = EnsureObject(policies, "ExtensionSettings");
                if (extensionSettings[addonId] is not JsonObject entry)
                {
                    extensionSettings[addonId] = new JsonObject
                    {
                        ["installation_mode"] = "force_installed",
                        ["install_url"] = installUrl,
                    };
                    if (TryWritePoliciesIfChanged(policyPath, root, out var created) && created)
                        changed = true;
                    continue;
                }

                var current = entry["install_url"]?.GetValue<string>();
                if (string.Equals(current, installUrl, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry["install_url"] = installUrl;
                if (TryWritePoliciesIfChanged(policyPath, root, out var policyChanged) && policyChanged)
                    changed = true;
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        return (errors, changed);
    }

    private static bool TryWritePoliciesIfChanged(string policyPath, JsonObject root, out bool changed)
    {
        changed = false;
        var next = FirefoxInstallRoots.SerializeIndented(root);
        if (File.Exists(policyPath))
        {
            var current = File.ReadAllText(policyPath).Replace("\r\n", "\n").Trim();
            var normalized = next.Replace("\r\n", "\n").Trim();
            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
        File.WriteAllText(policyPath, next);
        changed = true;
        return true;
    }

    private static void ApplyThirdPartySettings(JsonObject policies, string addonId, ImageShieldSettings settings)
    {
        var thirdParty = EnsureObject(policies, "3rdparty");
        var extensions = EnsureObject(thirdParty, "Extensions");
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        extensions[addonId] = new JsonObject
        {
            ["shieldActive"] = settings.ShieldActive,
            ["minSize"] = settings.MinSize,
            ["maxPerSecond"] = settings.MaxPerSecond,
            ["nsfwThreshold"] = settings.NsfwThreshold.ToString("0.##", culture),
            ["thumbThreshold"] = settings.ThumbThreshold.ToString("0.##", culture),
            ["sexyWeight"] = settings.SexyWeight.ToString("0.##", culture),
            ["uiMode"] = settings.UiMode,
        };
    }

    private static IEnumerable<string> FindInstallRoots() => FirefoxInstallRoots.All();

    private static List<string> ValidateDeployedXpi(string xpiPath, string installRoot)
    {
        var errors = new List<string>();

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(xpiPath);
            var manifest = archive.GetEntry("manifest.json");
            if (manifest is null)
            {
                errors.Add($"{installRoot}: deployed XPI is missing manifest.json at archive root.");
                return errors;
            }

            using var reader = new StreamReader(manifest.Open());
            var text = reader.ReadToEnd();
            if (!text.Contains("image-shield@guardi.app", StringComparison.Ordinal))
                errors.Add($"{installRoot}: deployed XPI manifest is missing the Firefox add-on ID.");
            if (text.Contains("\"manifest_version\": 3") || text.Contains("\"manifest_version\":3"))
                errors.Add($"{installRoot}: deployed XPI must be manifest_version 2 for unsigned sideload.");
        }
        catch (Exception ex)
        {
            errors.Add($"{installRoot}: XPI validation failed: {ex.Message}");
        }

        return errors;
    }

    private static List<string> ValidateDeployedManifest(string manifestPath, string installRoot)
    {
        var errors = new List<string>();

        try
        {
            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is not JsonObject manifest)
            {
                errors.Add($"{installRoot}: deployed manifest.json is not valid JSON.");
                return errors;
            }

            var version = manifest["manifest_version"]?.GetValue<int?>();
            if (version != 2)
                errors.Add($"{installRoot}: Firefox bundle must be manifest_version 2 (got {version?.ToString() ?? "null"}).");

            var geckoId = manifest["browser_specific_settings"]?["gecko"]?["id"]?.GetValue<string>()
                ?? manifest["applications"]?["gecko"]?["id"]?.GetValue<string>();
            if (!string.Equals(geckoId, "image-shield@guardi.app", StringComparison.Ordinal))
                errors.Add($"{installRoot}: deployed manifest is missing gecko add-on ID.");

            if (manifest.ContainsKey("action") || manifest.ContainsKey("host_permissions"))
                errors.Add($"{installRoot}: deployed manifest still contains Chromium-only MV3 keys.");

            if (manifest["icons"]?["48"]?.GetValue<string>() is { } icon48
                && icon48.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{installRoot}: Firefox install requires PNG icons, not SVG.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{installRoot}: manifest validation failed: {ex.Message}");
        }

        return errors;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            CopyDirectory(dir, Path.Combine(destDir, name));
        }
    }
}


