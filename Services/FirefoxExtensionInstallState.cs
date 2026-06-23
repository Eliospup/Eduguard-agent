using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EduGuardAgent.Services;

/// <summary>
/// Detects Guardi Firefox extension via distribution bundle, enterprise policy, or profile.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FirefoxExtensionInstallState
{
    private static readonly Regex UrlVersionPattern = new(
        @"extension-v(?<ver>\d+(?:\.\d+)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ExtensionPresence Probe(ProtectedBrowser browser, string addonId)
    {
        // Profile install only — enterprise policy or distribution XPI on disk does not
        // mean the extension is loaded in the user's profile yet (Firefox needs a restart).
        return ProbeProfile(browser, addonId);
    }

    public static bool HasDistributionOrPolicy(string addonId) =>
        TryReadDistributionBundle(addonId, out _)
        || TryReadEnterprisePolicy(addonId, out _);

    public static ExtensionPresence ProbeProfileInstall(ProtectedBrowser browser, string addonId) =>
        ProbeProfile(browser, addonId);

    private static bool TryReadDistributionBundle(string addonId, out string? version)
    {
        version = null;

        foreach (var root in FirefoxInstallRoots.All())
        {
            var unpacked = Path.Combine(root, "distribution", "extensions", addonId);
            if (Directory.Exists(unpacked))
            {
                version = ReadManifestVersion(Path.Combine(unpacked, "manifest.json"));
                return true;
            }

            var xpi = Path.Combine(root, "distribution", "extensions", addonId + ".xpi");
            if (!File.Exists(xpi))
                continue;

            version = ReadXpiManifestVersion(xpi);
            return true;
        }

        return false;
    }

    private static bool TryReadEnterprisePolicy(string addonId, out string? version)
    {
        version = null;

        foreach (var root in FirefoxInstallRoots.All())
        {
            var policyPath = FirefoxInstallRoots.PolicyPath(root);
            if (!File.Exists(policyPath))
                continue;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(policyPath)) is not JsonObject policyRoot)
                    continue;

                var policies = policyRoot["policies"] as JsonObject;
                var extensionSettings = policies?["ExtensionSettings"] as JsonObject;
                if (extensionSettings?[addonId] is not JsonObject entry)
                    continue;

                var mode = entry["installation_mode"]?.GetValue<string>();
                if (!string.Equals(mode, "force_installed", StringComparison.OrdinalIgnoreCase))
                    continue;

                version = VersionFromInstallUrl(entry["install_url"]?.GetValue<string>());
                return true;
            }
            catch
            {
                // Locked or corrupt policies.json — try next root.
            }
        }

        return false;
    }

    private static ExtensionPresence ProbeProfile(ProtectedBrowser browser, string addonId)
    {
        var canonicalXpi = FirefoxLocalPaths.CanonicalXpiPath(addonId);
        if (File.Exists(canonicalXpi))
        {
            foreach (var home in EnumerateUserHomes())
            {
                var profilesRoot = Path.Combine(home, browser.UserDataRelative);
                if (!Directory.Exists(profilesRoot))
                    continue;

                foreach (var profileDir in SafeEnumerateDirectories(profilesRoot))
                {
                    var extRoot = Path.Combine(profileDir, "extensions");
                    if (File.Exists(Path.Combine(extRoot, addonId + ".xpi"))
                        || Directory.Exists(Path.Combine(extRoot, addonId)))
                    {
                        return new ExtensionPresence(true, ReadProfileVersion(profileDir, addonId));
                    }
                }
            }
        }

        foreach (var home in EnumerateUserHomes())
        {
            var profilesRoot = Path.Combine(home, browser.UserDataRelative);
            if (!Directory.Exists(profilesRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(profilesRoot))
            {
                if (TryReadProfileAddon(profileDir, addonId, out var version))
                    return new ExtensionPresence(true, version);
            }
        }

        return ExtensionPresence.Absent;
    }

    private static bool TryReadProfileAddon(string profileDir, string addonId, out string? version)
    {
        version = null;

        var extDataDir = Path.Combine(profileDir, "browser-extension-data", addonId);
        if (Directory.Exists(extDataDir))
        {
            version = ReadProfileVersion(profileDir, addonId);
            return true;
        }

        var extRoot = Path.Combine(profileDir, "extensions");
        if (File.Exists(Path.Combine(extRoot, addonId))
            || Directory.Exists(Path.Combine(extRoot, addonId))
            || File.Exists(Path.Combine(extRoot, addonId + ".xpi")))
        {
            version = ReadProfileVersion(profileDir, addonId);
            return true;
        }

        return ReadFromExtensionsJson(profileDir, addonId, out version);
    }

    private static string? ReadProfileVersion(string profileDir, string addonId) =>
        ReadFromExtensionsJson(profileDir, addonId, out var version) ? version : null;

    private static bool ReadFromExtensionsJson(string profileDir, string addonId, out string? version)
    {
        version = null;
        var extJsonPath = Path.Combine(profileDir, "extensions.json");
        if (!File.Exists(extJsonPath))
            return false;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(extJsonPath)) is not JsonObject root
                || root["addons"] is not JsonArray addons)
            {
                return false;
            }

            foreach (var addon in addons)
            {
                var id = addon?["id"]?.GetValue<string>();
                if (!string.Equals(id, addonId, StringComparison.OrdinalIgnoreCase))
                    continue;

                version = addon?["version"]?.GetValue<string>();
                return true;
            }
        }
        catch
        {
            // Corrupt or locked.
        }

        return false;
    }

    private static string? ReadManifestVersion(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return null;

            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is JsonObject manifest)
                return manifest["version"]?.GetValue<string>();
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? ReadXpiManifestVersion(string xpiPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(xpiPath);
            var entry = zip.GetEntry("manifest.json");
            if (entry is null)
                return null;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            if (JsonNode.Parse(reader.ReadToEnd()) is JsonObject manifest)
                return manifest["version"]?.GetValue<string>();
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? VersionFromInstallUrl(string? installUrl)
    {
        if (string.IsNullOrWhiteSpace(installUrl))
            return null;

        var match = UrlVersionPattern.Match(installUrl);
        return match.Success ? match.Groups["ver"].Value : null;
    }

    private static IEnumerable<string> EnumerateUserHomes()
    {
        var usersRoot = Path.GetDirectoryName(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (string.IsNullOrWhiteSpace(usersRoot) || !Directory.Exists(usersRoot))
            yield break;

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Public", "Default", "Default User", "All Users", "defaultuser0",
        };

        foreach (var dir in SafeEnumerateDirectories(usersRoot))
        {
            var name = Path.GetFileName(dir);
            if (!skip.Contains(name))
                yield return dir;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return [];
        }
    }
}
