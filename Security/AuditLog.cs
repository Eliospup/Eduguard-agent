namespace EduGuardAgent.Security;

internal static class AuditLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        Config.AuditLogFileName);

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

        lock (Gate)
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            RotateIfNeeded();
            File.AppendAllText(LogPath, line);
        }
    }

    public static IReadOnlyList<string> ReadRecentLines(int maxLines = 30, params string[] keywords)
    {
        if (maxLines <= 0)
            return [];

        lock (Gate)
        {
            try
            {
                var allLines = new List<string>();
                foreach (var path in EnumerateLogFilesOldestFirst())
                {
                    if (!File.Exists(path))
                        continue;

                    allLines.AddRange(File.ReadAllLines(path));
                }

                if (allLines.Count == 0)
                    return [];

                IEnumerable<string> query = allLines;
                if (keywords.Length > 0)
                {
                    query = allLines.Where(line =>
                        keywords.Any(keyword =>
                            line.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
                }

                return query.TakeLast(maxLines).ToList();
            }
            catch
            {
                return [];
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath))
            return;

        if (new FileInfo(LogPath).Length < Config.AuditLogMaxBytes)
            return;

        var dir = Path.GetDirectoryName(LogPath)!;
        var baseName = Config.AuditLogFileName;
        var oldest = Path.Combine(dir, $"{baseName}.{Config.AuditLogArchivedFiles}");
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (var i = Config.AuditLogArchivedFiles - 1; i >= 1; i--)
        {
            var from = Path.Combine(dir, i == 1 ? baseName : $"{baseName}.{i}");
            var to = Path.Combine(dir, $"{baseName}.{i + 1}");
            if (!File.Exists(from))
                continue;

            File.Move(from, to, overwrite: true);
        }

        File.WriteAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] audit log rotated{Environment.NewLine}");
    }

    private static IEnumerable<string> EnumerateLogFilesOldestFirst()
    {
        var dir = Path.GetDirectoryName(LogPath)!;
        var baseName = Config.AuditLogFileName;

        for (var i = Config.AuditLogArchivedFiles; i >= 1; i--)
        {
            var archived = Path.Combine(dir, $"{baseName}.{i}");
            if (File.Exists(archived))
                yield return archived;
        }

        yield return LogPath;
    }
}
