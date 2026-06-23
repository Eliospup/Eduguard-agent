using System.Security.Cryptography;
using System.Text;

namespace EduGuardAgent.Services;

/// <summary>Compares extension folders so Guardi only restarts Firefox when files actually changed.</summary>
internal static class ExtensionBundleSync
{
    public static string Fingerprint(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            return string.Empty;

        using var sha = SHA256.Create();
        var builder = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            builder.Append(relative);
            builder.Append('\0');
            builder.Append(Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(file))));
            builder.Append('\n');
        }

        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public static bool SyncIfChanged(string sourceDir, string destDir, out bool changed)
    {
        changed = false;

        if (!Directory.Exists(sourceDir))
            return false;

        var sourceFingerprint = Fingerprint(sourceDir);
        if (string.IsNullOrEmpty(sourceFingerprint))
            return false;

        var destFingerprint = Directory.Exists(destDir) ? Fingerprint(destDir) : string.Empty;
        if (string.Equals(sourceFingerprint, destFingerprint, StringComparison.Ordinal))
            return true;

        changed = true;
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);

        CopyDirectory(sourceDir, destDir);
        return true;
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
}
