using System.Runtime.Versioning;
using System.Text.Json;

namespace EduGuardAgent.Services;

/// <summary>
/// Detects whether Chromium has actually registered an extension in profile prefs.
/// Unpacked folders copied under Extensions/ are not enough — Chrome must list the ID
/// in Preferences / Secure Preferences.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ChromiumExtensionInstallState
{
    public static bool IsInstalled(ProtectedBrowser browser, string extensionId)
    {
        foreach (var home in EnumerateUserHomes())
        {
            var userDataRoot = Path.Combine(home, browser.UserDataRelative);
            if (!Directory.Exists(userDataRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(userDataRoot))
            {
                if (IsProfileDirectory(profileDir) && IsRegisteredInProfile(profileDir, extensionId))
                    return true;
            }
        }

        return false;
    }

    public static string? GetInstalledVersion(ProtectedBrowser browser, string extensionId)
    {
        string? best = null;

        foreach (var home in EnumerateUserHomes())
        {
            var userDataRoot = Path.Combine(home, browser.UserDataRelative);
            if (!Directory.Exists(userDataRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(userDataRoot))
            {
                if (!IsProfileDirectory(profileDir))
                    continue;

                var version = ReadVersionFromProfile(profileDir, extensionId);
                if (version is not null && (best is null || ExtensionPresenceProbe.CompareVersions(version, best) > 0))
                    best = version;
            }
        }

        return best;
    }

    public static bool IsRegisteredInProfile(string profileDir, string extensionId)
    {
        foreach (var prefsName in new[] { "Preferences", "Secure Preferences" })
        {
            var path = Path.Combine(profileDir, prefsName);
            if (!File.Exists(path))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("extensions", out var extensions)
                    || !extensions.TryGetProperty("settings", out var settings)
                    || !settings.TryGetProperty(extensionId, out var entry))
                {
                    continue;
                }

                if (entry.TryGetProperty("state", out var state))
                    return true;
            }
            catch
            {
                // Locked or corrupt prefs — try next file.
            }
        }

        return false;
    }

    public static int CleanupUnregisteredSideloads(string extensionId)
    {
        var removed = 0;

        foreach (var browser in BrowserCatalog.Protected.Where(b => b.Engine == BrowserEngine.Chromium))
        {
            foreach (var home in EnumerateUserHomes())
            {
                var userDataRoot = Path.Combine(home, browser.UserDataRelative);
                if (!Directory.Exists(userDataRoot))
                    continue;

                foreach (var profileDir in SafeEnumerateDirectories(userDataRoot))
                {
                    if (!IsProfileDirectory(profileDir))
                        continue;

                    if (IsRegisteredInProfile(profileDir, extensionId))
                        continue;

                    var extRoot = Path.Combine(profileDir, "Extensions", extensionId);
                    if (!Directory.Exists(extRoot))
                        continue;

                    try
                    {
                        Directory.Delete(extRoot, recursive: true);
                        removed++;
                    }
                    catch
                    {
                        // Best effort — Chrome may hold locks while running.
                    }
                }
            }
        }

        return removed;
    }

    private static string? ReadVersionFromProfile(string profileDir, string extensionId)
    {
        foreach (var prefsName in new[] { "Preferences", "Secure Preferences" })
        {
            var path = Path.Combine(profileDir, prefsName);
            if (!File.Exists(path))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("extensions", out var extensions)
                    || !extensions.TryGetProperty("settings", out var settings)
                    || !settings.TryGetProperty(extensionId, out var entry))
                {
                    continue;
                }

                if (entry.TryGetProperty("manifest", out var manifest)
                    && manifest.TryGetProperty("version", out var version))
                {
                    return version.GetString();
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static bool IsProfileDirectory(string profileDir)
    {
        var name = Path.GetFileName(profileDir);
        if (name.Equals("System Profile", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Crashpad", StringComparison.OrdinalIgnoreCase)
            || name.Equals("External Extensions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(Path.Combine(profileDir, "Preferences"))
            || File.Exists(Path.Combine(profileDir, "Secure Preferences"));
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
