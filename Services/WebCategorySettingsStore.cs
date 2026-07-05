using System.Text.Json;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Persists which web-content categories are blocked. Stored encrypted + tamper-evident in the
/// hardened secure-state folder so a supervised user can't disable the adult-content filter by
/// editing the file. Only the enabled category keys are persisted — the curated domains are
/// recomputed from the catalog each session. Fails closed (everything blocked) on tampering.
/// </summary>
internal sealed class WebCategorySettingsStore
{
    private const string FileName = "web_categories.json";

    public HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Tampered)
        {
            AuditLog.Write("SECURITY: web-category settings failed integrity check — failing closed to all categories blocked.");
            foreach (var category in WebCategoryCatalog.All)
                set.Add(category.Key);
            return set;
        }

        if (status != StateReadStatus.Ok)
            return set;

        try
        {
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
        var keys = enabledKeys
            .Where(WebCategoryCatalog.IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SecureStateFile.Write(FileName, JsonSerializer.Serialize(keys));
    }
}
