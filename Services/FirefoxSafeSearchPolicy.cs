using System.Text.Json.Nodes;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class FirefoxSafeSearchPolicy
{
    private const string PolicyFileName = "policies.json";

    private static readonly JsonObject SafeSearchTemplate = (JsonNode.Parse("""
        {
          "policies": {
            "SearchEngines": {
              "Add": [
                {
                  "Name": "Google (SafeSearch)",
                  "URLTemplate": "https://www.google.com/search?q={searchTerms}&safe=active",
                  "Method": "GET"
                }
              ],
              "Default": "Google (SafeSearch)",
              "PreventInstalls": true
            },
            "Preferences": {
              "browser.search.update": {
                "Value": false,
                "Status": "locked"
              },
              "browser.search.suggest.enabled": {
                "Value": false,
                "Status": "locked"
              }
            }
          }
        }
        """) as JsonObject)!["policies"] as JsonObject ?? new JsonObject();

    public (List<FirefoxPolicyBackup> Backups, List<string> Errors) Apply(SafeSearchBackupStore backupStore)
    {
        var backups = new List<FirefoxPolicyBackup>();
        var errors = new List<string>();

        foreach (var installRoot in FirefoxInstallRoots.All())
        {
            var policyPath = FirefoxInstallRoots.PolicyPath(installRoot);

            try
            {
                var backup = new FirefoxPolicyBackup
                {
                    PolicyPath = policyPath,
                    HadFile = File.Exists(policyPath),
                };

                if (backup.HadFile)
                {
                    var backupPath = backupStore.BackupFilePath(
                        SanitizeLabel(installRoot),
                        $"{PolicyFileName}.bak");
                    File.Copy(policyPath, backupPath, overwrite: true);
                    backup.BackupPath = backupPath;
                }

                var root = FirefoxInstallRoots.ReadOrCreate(policyPath);
                var policies = FirefoxInstallRoots.EnsureObject(root, "policies");

                if (SafeSearchTemplate["SearchEngines"] is JsonNode searchEngines)
                    policies["SearchEngines"] = searchEngines.DeepClone();

                if (SafeSearchTemplate["Preferences"] is JsonObject safeSearchPrefs)
                {
                    var preferences = FirefoxInstallRoots.EnsureObject(policies, "Preferences");
                    foreach (var (key, value) in safeSearchPrefs)
                        preferences[key] = value?.DeepClone();
                }

                FirefoxInstallRoots.WriteIndented(policyPath, root);
                backups.Add(backup);
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        if (backups.Count == 0)
            errors.Add("Firefox install folder not found on this PC.");

        return (backups, errors);
    }

    public List<string> Restore(IReadOnlyList<FirefoxPolicyBackup> backups)
    {
        var errors = new List<string>();

        foreach (var backup in backups)
        {
            try
            {
                if (backup.HadFile)
                {
                    if (backup.BackupPath is null || !File.Exists(backup.BackupPath))
                    {
                        errors.Add($"Missing Firefox policy backup for {backup.PolicyPath}");
                        continue;
                    }

                    File.Copy(backup.BackupPath, backup.PolicyPath, overwrite: true);
                    File.Delete(backup.BackupPath);
                }
                else if (File.Exists(backup.PolicyPath))
                {
                    File.Delete(backup.PolicyPath);
                    TryDeleteEmptyDistributionFolder(backup.PolicyPath);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{backup.PolicyPath}: {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Removes only the SafeSearch blocks we add, keeping other policy entries (e.g. extensions).
    /// </summary>
    public List<string> ForceRemove()
    {
        var errors = new List<string>();

        foreach (var installRoot in FirefoxInstallRoots.All())
        {
            var policyPath = FirefoxInstallRoots.PolicyPath(installRoot);
            if (!File.Exists(policyPath))
                continue;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(policyPath)) is not JsonObject root
                    || root["policies"] is not JsonObject policies)
                {
                    continue;
                }

                policies.Remove("SearchEngines");

                if (policies["Preferences"] is JsonObject preferences)
                {
                    preferences.Remove("browser.search.update");
                    preferences.Remove("browser.search.suggest.enabled");
                    if (preferences.Count == 0)
                        policies.Remove("Preferences");
                }

                if (policies.Count == 0)
                    root.Remove("policies");

                if (root.Count == 0)
                {
                    File.Delete(policyPath);
                    TryDeleteEmptyDistributionFolder(policyPath);
                }
                else
                {
                    FirefoxInstallRoots.WriteIndented(policyPath, root);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{policyPath}: {ex.Message}");
            }
        }

        return errors;
    }

    public bool HasActivePolicies()
    {
        foreach (var installRoot in FirefoxInstallRoots.All())
        {
            var policyPath = FirefoxInstallRoots.PolicyPath(installRoot);
            if (!File.Exists(policyPath))
                continue;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(policyPath)) is not JsonObject root
                    || root["policies"] is not JsonObject policies)
                {
                    continue;
                }

                if (policies["SearchEngines"] is not null || policies["Preferences"] is not null)
                    return true;
            }
            catch
            {
                // ignored
            }
        }

        return false;
    }

    private static void TryDeleteEmptyDistributionFolder(string policyPath)
    {
        var distributionDir = Path.GetDirectoryName(policyPath);
        if (distributionDir is null || !Directory.Exists(distributionDir))
            return;

        if (!Directory.EnumerateFileSystemEntries(distributionDir).Any())
            Directory.Delete(distributionDir);
    }

    private static string SanitizeLabel(string path) =>
        Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(path));
}
