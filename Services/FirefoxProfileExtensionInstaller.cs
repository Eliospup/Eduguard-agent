using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EduGuardAgent.Services;

/// <summary>
/// Sideloads the Guardi extension into every Firefox profile. Uses Mozilla's
/// pointer-file format (extensions/{id} text file → path) plus a full unpacked
/// copy as fallback.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FirefoxProfileExtensionInstaller
{
    private const string ProfilesRelative = @"AppData\Roaming\Mozilla\Firefox\Profiles";

    public static (List<string> ProfileXpiPaths, List<string> Errors) DeployProfileXpi(string addonId, string xpiSourcePath)
    {
        var deployed = new List<string>();
        var errors = new List<string>();

        if (!File.Exists(xpiSourcePath))
        {
            errors.Add($"Firefox profile XPI source missing at {xpiSourcePath}");
            return (deployed, errors);
        }

        foreach (var home in EnumerateUserHomes())
        {
            var profilesRoot = Path.Combine(home, ProfilesRelative);
            if (!Directory.Exists(profilesRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(profilesRoot))
            {
                try
                {
                    var extRoot = Path.Combine(profileDir, "extensions");
                    Directory.CreateDirectory(extRoot);
                    ClearConflictingSideloadArtifacts(extRoot, addonId);

                    var destPath = Path.Combine(extRoot, addonId + ".xpi");
                    File.Copy(xpiSourcePath, destPath, overwrite: true);
                    deployed.Add(destPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{profileDir}: {ex.Message}");
                }
            }
        }

        if (deployed.Count == 0)
            errors.Add("No Firefox profiles found for XPI sideload.");

        return (deployed, errors);
    }

    public static (List<string> DeployedPaths, List<string> Errors) DeployPointer(
        string addonId,
        string extensionDir)
    {
        var deployed = new List<string>();
        var errors = new List<string>();

        if (!Directory.Exists(extensionDir)
            || !File.Exists(Path.Combine(extensionDir, "manifest.json")))
        {
            errors.Add($"Firefox pointer target missing at {extensionDir}");
            return (deployed, errors);
        }

        var targetPath = Path.GetFullPath(extensionDir);

        foreach (var home in EnumerateUserHomes())
        {
            var profilesRoot = Path.Combine(home, ProfilesRelative);
            if (!Directory.Exists(profilesRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(profilesRoot))
            {
                try
                {
                    var extRoot = Path.Combine(profileDir, "extensions");
                    Directory.CreateDirectory(extRoot);
                    ClearConflictingSideloadArtifacts(extRoot, addonId);

                    var pointerPath = Path.Combine(extRoot, addonId);
                    File.WriteAllText(pointerPath, targetPath);
                    deployed.Add(pointerPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{profileDir}: {ex.Message}");
                }
            }
        }

        if (deployed.Count == 0)
            errors.Add("No Firefox profiles found for pointer sideload.");

        return (deployed, errors);
    }

    public static (List<string> DeployedDirs, bool Changed, List<string> Errors) DeployUnpacked(string addonId, string sourceDir)
    {
        var deployed = new List<string>();
        var errors = new List<string>();
        var changed = false;

        if (!Directory.Exists(sourceDir) || !File.Exists(Path.Combine(sourceDir, "manifest.json")))
        {
            errors.Add($"Firefox unpacked source missing at {sourceDir}");
            return (deployed, changed, errors);
        }

        foreach (var home in EnumerateUserHomes())
        {
            var profilesRoot = Path.Combine(home, ProfilesRelative);
            if (!Directory.Exists(profilesRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(profilesRoot))
            {
                try
                {
                    var extRoot = Path.Combine(profileDir, "extensions");
                    Directory.CreateDirectory(extRoot);
                    ClearConflictingSideloadArtifacts(extRoot, addonId);

                    var destDir = Path.Combine(extRoot, addonId);
                    if (!ExtensionBundleSync.SyncIfChanged(sourceDir, destDir, out var bundleChanged))
                        errors.Add($"{profileDir}: failed to sync unpacked extension.");
                    else if (bundleChanged)
                        changed = true;

                    deployed.Add(destDir);
                }
                catch (Exception ex)
                {
                    errors.Add($"{profileDir}: {ex.Message}");
                }
            }
        }

        if (deployed.Count == 0)
            errors.Add("No Firefox profiles found to sideload the shield into.");

        return (deployed, changed, errors);
    }

    public static List<string> RemoveUnpacked(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            return [$"{directory}: {ex.Message}"];
        }

        return [];
    }

    public static List<string> RemoveDeployed(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            return [$"{path}: {ex.Message}"];
        }

        return [];
    }

    /// <summary>
    /// Clears cached enterprise add-on state so Firefox re-fetches the policy XPI on next launch.
    /// </summary>
    public static (int ProfilesTouched, List<string> Errors) PurgeAddonFromAllProfiles(string addonId)
    {
        var touched = 0;
        var errors = new List<string>();

        foreach (var home in EnumerateUserHomes())
        {
            var profilesRoot = Path.Combine(home, ProfilesRelative);
            if (!Directory.Exists(profilesRoot))
                continue;

            foreach (var profileDir in SafeEnumerateDirectories(profilesRoot))
            {
                try
                {
                    var changed = false;
                    var extRoot = Path.Combine(profileDir, "extensions");
                    if (Directory.Exists(extRoot))
                    {
                        ClearConflictingSideloadArtifacts(extRoot, addonId);
                        changed = true;
                    }

                    if (PurgeFromExtensionsJson(profileDir, addonId))
                        changed = true;

                    var dataDir = Path.Combine(profileDir, "browser-extension-data", addonId);
                    if (Directory.Exists(dataDir))
                    {
                        Directory.Delete(dataDir, recursive: true);
                        changed = true;
                    }

                    if (changed)
                        touched++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{profileDir}: {ex.Message}");
                }
            }
        }

        return (touched, errors);
    }

    private static bool PurgeFromExtensionsJson(string profileDir, string addonId)
    {
        var path = Path.Combine(profileDir, "extensions.json");
        if (!File.Exists(path))
            return false;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
                return false;

            if (root["addons"] is not JsonArray addons)
                return false;

            var removed = false;
            for (var i = addons.Count - 1; i >= 0; i--)
            {
                var id = addons[i]?["id"]?.GetValue<string>();
                if (!string.Equals(id, addonId, StringComparison.OrdinalIgnoreCase))
                    continue;

                addons.RemoveAt(i);
                removed = true;
            }

            if (!removed)
                return false;

            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearConflictingSideloadArtifacts(string extRoot, string addonId)
    {
        var legacyXpi = Path.Combine(extRoot, addonId + ".xpi");
        if (File.Exists(legacyXpi))
            File.Delete(legacyXpi);

        var unpackedDir = Path.Combine(extRoot, addonId);
        if (Directory.Exists(unpackedDir))
            Directory.Delete(unpackedDir, recursive: true);

        var pointerPath = Path.Combine(extRoot, addonId);
        if (File.Exists(pointerPath))
            File.Delete(pointerPath);
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
