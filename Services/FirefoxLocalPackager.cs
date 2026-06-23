using System.IO.Compression;

namespace EduGuardAgent.Services;

/// <summary>Packs <c>extension/dist/firefox</c> into an unsigned XPI for local policy install.</summary>
internal static class FirefoxLocalPackager
{
    public const string LocalXpiFileName = "guardi-image-shield-local.xpi";

    public static string? FindFirefoxDistDir()
    {
        var root = ExtensionBundleLocator.FindExtensionSourceRoot();
        if (root is null)
            return null;

        var dist = Path.Combine(root, "dist", "firefox");
        return File.Exists(Path.Combine(dist, "manifest.json")) ? dist : null;
    }

    public static bool HasSource() => FindFirefoxDistDir() is not null;

    public static (string? XpiPath, List<string> Errors) EnsureXpi()
    {
        var dist = FindFirefoxDistDir();
        if (dist is null)
        {
            return (null,
            [
                "Firefox extension not built. Run: cd extension && npm.cmd run build:firefox",
            ]);
        }

        var root = ExtensionBundleLocator.FindExtensionSourceRoot()!;
        var outDir = Path.Combine(root, "web-ext-output");
        Directory.CreateDirectory(outDir);
        var xpiPath = Path.Combine(outDir, LocalXpiFileName);

        try
        {
            if (File.Exists(xpiPath))
                File.Delete(xpiPath);

            PackDirectory(dist, xpiPath);

            if (!ValidateXpi(xpiPath, out var validationError))
                return (null, [validationError]);

            return (xpiPath, []);
        }
        catch (Exception ex)
        {
            return (null, [$"Failed to pack Firefox XPI: {ex.Message}"]);
        }
    }

    private static void PackDirectory(string sourceDir, string xpiPath)
    {
        using var archive = ZipFile.Open(xpiPath, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');

            archive.CreateEntryFromFile(file, relative, CompressionLevel.NoCompression);
        }
    }

    private static bool ValidateXpi(string xpiPath, out string error)
    {
        error = string.Empty;

        try
        {
            using var archive = ZipFile.OpenRead(xpiPath);
            var manifest = archive.GetEntry("manifest.json");
            if (manifest is null)
            {
                error = "Packed XPI is missing manifest.json at archive root.";
                return false;
            }

            using var reader = new StreamReader(manifest.Open());
            var text = reader.ReadToEnd();
            if (!text.Contains("image-shield@guardi.app", StringComparison.Ordinal))
            {
                error = "Packed XPI manifest is missing the Firefox add-on ID.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"Packed XPI validation failed: {ex.Message}";
            return false;
        }
    }
}
