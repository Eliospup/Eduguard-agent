using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace EduGuardAgent.Services;

/// <summary>Shared Firefox install roots and policies.json helpers.</summary>
internal static class FirefoxInstallRoots
{
    public static IEnumerable<string> All()
    {
        if (FirefoxEditionHelper.UseSignedReleaseTarget)
            return FirefoxEditionHelper.FindReleaseInstallRoots();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in new[]
                 {
                     @"C:\Program Files\Mozilla Firefox",
                     @"C:\Program Files (x86)\Mozilla Firefox",
                     @"C:\Program Files\Firefox Developer Edition",
                     @"C:\Program Files (x86)\Firefox Developer Edition",
                 })
        {
            if (Directory.Exists(path))
                roots.Add(path);
        }

        foreach (var root in FirefoxEditionHelper.FindDeveloperEditionInstallRoots())
            roots.Add(root);

        using (var appPath = Registry.LocalMachine.OpenSubKey(
                   @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe"))
        {
            if (appPath?.GetValue(null) is string exePath && !string.IsNullOrWhiteSpace(exePath))
            {
                var dir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    roots.Add(dir);
            }
        }

        using var mozillaKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Mozilla\Mozilla Firefox");
        if (mozillaKey is not null)
        {
            foreach (var version in mozillaKey.GetSubKeyNames())
            {
                using var versionKey = mozillaKey.OpenSubKey(version);
                var main = versionKey?.GetValue("Main") as string;
                if (!string.IsNullOrWhiteSpace(main))
                {
                    var dir = Path.GetDirectoryName(main);
                    if (!string.IsNullOrWhiteSpace(dir))
                        roots.Add(dir);
                }
            }
        }

        return roots.Where(Directory.Exists);
    }

    public static string PolicyPath(string installRoot) =>
        Path.Combine(installRoot, "distribution", "policies.json");

    public static JsonObject ReadOrCreate(string policyPath)
    {
        if (File.Exists(policyPath))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(policyPath)) is JsonObject existing)
                    return existing;
            }
            catch
            {
                // Corrupt file — start fresh.
            }
        }

        return new JsonObject();
    }

    public static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject obj)
            return obj;

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    public static void WriteIndented(string policyPath, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
        File.WriteAllText(policyPath, SerializeIndented(root));
    }

    public static string SerializeIndented(JsonObject root) =>
        root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}
