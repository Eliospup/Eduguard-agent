using System.Runtime.Versioning;
using System.Text.Json;

namespace EduGuardAgent.Services;

/// <summary>
/// Writes Chromium external-extension manifests so policy/local update URLs can install
/// the CRX on the next browser launch. Does not copy unpacked folders into Extensions/
/// (that fooled presence detection without Chrome ever loading the add-on).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ChromiumProfileExtensionInstaller
{
    public static (List<string> DeployedPaths, bool FilesChanged, List<string> Errors) DeployExternalUpdateUrl(
        string extensionId,
        string updateUrl)
    {
        var deployed = new List<string>();
        var errors = new List<string>();
        var filesChanged = false;

        if (string.IsNullOrWhiteSpace(extensionId) || string.IsNullOrWhiteSpace(updateUrl))
        {
            errors.Add("Chromium external extension manifest is missing id or update URL.");
            return (deployed, false, errors);
        }

        var payload = JsonSerializer.Serialize(new { external_update_url = updateUrl });

        foreach (var browser in BrowserCatalog.Protected.Where(b => b.Engine == BrowserEngine.Chromium))
        {
            foreach (var home in EnumerateUserHomes())
            {
                var userDataRoot = Path.Combine(home, browser.UserDataRelative);
                if (!Directory.Exists(userDataRoot))
                    continue;

                try
                {
                    var externalDir = Path.Combine(userDataRoot, "External Extensions");
                    Directory.CreateDirectory(externalDir);
                    var jsonPath = Path.Combine(externalDir, extensionId + ".json");

                    var changed = !File.Exists(jsonPath) || File.ReadAllText(jsonPath) != payload;
                    if (changed)
                    {
                        File.WriteAllText(jsonPath, payload);
                        filesChanged = true;
                    }

                    deployed.Add(jsonPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{browser.DisplayName} {userDataRoot}: {ex.Message}");
                }
            }
        }

        if (deployed.Count == 0)
            errors.Add("No Chromium user-data folders found for external extension manifest.");

        return (deployed, filesChanged, errors);
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
