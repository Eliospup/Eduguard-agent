using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Services;

/// <summary>
/// Persists which web-content categories are blocked. Lives under %APPDATA%\EduGuard,
/// so it is removed cleanly on uninstall (nothing stays blocked). The curated domains
/// themselves are never persisted here — only the enabled category keys — so the list
/// stays small and is recomputed from the catalog each session.
/// </summary>
internal sealed class WebCategorySettingsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "web_categories.json");

    public HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(StorePath))
            return set;

        try
        {
            var json = File.ReadAllText(StorePath);
            var keys = JsonSerializer.Deserialize<List<string>>(json);
            if (keys is null)
                return set;

            foreach (var key in keys)
            {
                if (WebCategoryCatalog.IsKnown(key))
                    set.Add(key);
            }
        }
        catch
        {
            // Corrupt store — start with nothing blocked.
        }

        return set;
    }

    public void Save(IEnumerable<string> enabledKeys)
    {
        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        var keys = enabledKeys
            .Where(WebCategoryCatalog.IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        File.WriteAllText(StorePath, JsonSerializer.Serialize(keys));
    }
}
