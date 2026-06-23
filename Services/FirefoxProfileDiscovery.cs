using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

/// <summary>Reads profiles.ini so deploy logs show which Firefox profile is active.</summary>
[SupportedOSPlatform("windows")]
internal static class FirefoxProfileDiscovery
{
    private const string ProfilesRelative = @"AppData\Roaming\Mozilla\Firefox";

    public static string DescribeProfilesForAudit()
    {
        var lines = new List<string>();

        foreach (var home in EnumerateUserHomes())
        {
            var iniPath = Path.Combine(home, ProfilesRelative, "profiles.ini");
            if (!File.Exists(iniPath))
                continue;

            try
            {
                var sections = ParseIni(File.ReadAllLines(iniPath));
                var profilesRoot = Path.Combine(home, ProfilesRelative);
                foreach (var section in sections)
                {
                    if (!section.TryGetValue("Path", out var relativePath) || relativePath.Length == 0)
                        continue;

                    var isRelative = !section.TryGetValue("IsRelative", out var relFlag)
                        || !string.Equals(relFlag, "0", StringComparison.Ordinal);
                    var profileDir = isRelative
                        ? Path.Combine(profilesRoot, relativePath)
                        : relativePath;

                    var name = section.TryGetValue("Name", out var profileName) ? profileName : "(unnamed)";
                    var isDefault = section.TryGetValue("Default", out var defFlag)
                        && string.Equals(defFlag, "1", StringComparison.Ordinal);
                    var marker = isDefault ? " [DEFAULT]" : "";
                    lines.Add($"{name}{marker} -> {profileDir}");
                }
            }
            catch (Exception ex)
            {
                lines.Add($"{iniPath}: {ex.Message}");
            }
        }

        return lines.Count == 0 ? "no Firefox profiles.ini found" : string.Join("; ", lines);
    }

    private static List<Dictionary<string, string>> ParseIni(IEnumerable<string> lines)
    {
        var sections = new List<Dictionary<string, string>>();
        Dictionary<string, string>? current = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections.Add(current);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null)
                continue;

            current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return sections;
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

        foreach (var dir in Directory.EnumerateDirectories(usersRoot))
        {
            var name = Path.GetFileName(dir);
            if (!skip.Contains(name))
                yield return dir;
        }
    }
}
