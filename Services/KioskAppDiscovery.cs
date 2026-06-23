using EduGuardAgent.Models;

namespace EduGuardAgent.Services;

internal sealed record DiscoveredKioskApp(
    string CatalogId,
    string Name,
    string Path,
    string? Args,
    string Icon);

internal static class KioskAppDiscovery
{
    public static IReadOnlyList<DiscoveredKioskApp> DiscoverAll()
    {
        var results = new List<DiscoveredKioskApp>();

        foreach (var definition in KioskCommonAppCatalog.All)
        {
            var path = ResolveDefinition(definition);
            if (path is null)
                continue;

            results.Add(new DiscoveredKioskApp(
                definition.Id,
                definition.Name,
                path,
                definition.DefaultArgs,
                definition.Icon));
        }

        return results;
    }

    private static string? ResolveDefinition(KioskCommonAppDefinition definition)
    {
        foreach (var template in definition.CandidatePaths)
        {
            var path = ResolveTemplate(template);
            if (path is not null)
                return path;
        }

        if (definition.SearchRoots is null || definition.SearchRoots.Count == 0)
            return null;

        foreach (var rootTemplate in definition.SearchRoots)
        {
            var root = ExpandFolderTemplate(rootTemplate);
            if (root is null || !Directory.Exists(root))
                continue;

            var found = FindExe(root, definition.ExeFileName, maxDepth: 4);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string? ResolveTemplate(string template)
    {
        if (template.Contains('*', StringComparison.Ordinal))
            return FindGlob(template);

        var expanded = ExpandPath(template);
        return File.Exists(expanded) ? expanded : null;
    }

    private static string? ExpandFolderTemplate(string template)
    {
        if (!template.Contains('*', StringComparison.Ordinal))
            return ExpandPath(template);

        var slash = template.LastIndexOfAny(['\\', '/']);
        if (slash < 0)
            return null;

        var parentTemplate = template[..slash];
        var childPattern = template[(slash + 1)..];
        var parentGlob = FindGlob(parentTemplate);
        if (parentGlob is null)
            return null;

        var parentDir = Path.GetDirectoryName(parentGlob);
        if (parentDir is null || !Directory.Exists(parentDir))
            return null;

        foreach (var dir in Directory.EnumerateDirectories(parentDir, childPattern))
            return dir;

        return null;
    }

    private static string? FindGlob(string template)
    {
        var slash = template.LastIndexOfAny(['\\', '/']);
        if (slash < 0)
            return null;

        var dirTemplate = template[..slash];
        var filePattern = template[(slash + 1)..];
        var dir = ExpandFolderTemplate(dirTemplate);
        if (dir is null || !Directory.Exists(dir))
            dir = ExpandPath(dirTemplate);

        if (!Directory.Exists(dir))
            return null;

        foreach (var file in Directory.EnumerateFiles(dir, filePattern))
            return file;

        return null;
    }

    private static string? FindExe(string root, string exeFileName, int maxDepth)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, exeFileName, SearchOption.AllDirectories))
            {
                if (GetDepth(root, file) <= maxDepth)
                    return file;
            }
        }
        catch
        {
            // Access denied or transient IO errors — skip this root.
        }

        return null;
    }

    private static int GetDepth(string root, string filePath)
    {
        var relative = Path.GetRelativePath(root, filePath);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
    }

    private static string ExpandPath(string template)
    {
        var expanded = template;
        expanded = ReplaceFolder(expanded, "%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        expanded = ReplaceFolder(expanded, "%ProgramFiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        expanded = ReplaceFolder(expanded, "%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        expanded = ReplaceFolder(expanded, "%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        expanded = ReplaceFolder(expanded, "%SystemRoot%", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        return expanded;
    }

    private static string ReplaceFolder(string input, string token, string value) =>
        input.Replace(token, value.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
