namespace EduGuardAgent.Services;

/// <summary>Finds the extension source tree next to the agent.</summary>
internal static class ExtensionBundleLocator
{
    public static string? FindExtensionSourceRoot()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "extension"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "extension")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "extension")),
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir.Parent is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "extension"));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(path, "package.json")))
                return path;
        }

        return null;
    }
}
