using System.Net;
using System.Net.Http;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Downloads the signed Firefox XPI for Guardi (bypassing the system proxy) and caches it
/// under %AppData%\EduGuard. Falls back to an existing cache file or distribution bundle
/// when the GitHub release URL is missing or stale.
/// </summary>
internal static class FirefoxSignedXpiCache
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "firefox-xpi-cache");

    public static (string? CachedPath, List<string> Errors) EnsureCached(
        string downloadUrl,
        string version,
        string addonId = Config.ImageShieldFirefoxAddonId)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(downloadUrl))
            errors.Add("Firefox install URL is empty.");

        Directory.CreateDirectory(CacheDir);
        var safeVersion = string.IsNullOrWhiteSpace(version) ? "latest" : version.Replace('.', '_');
        var cachedPath = Path.Combine(CacheDir, $"guardi-image-shield-{safeVersion}.xpi");

        if (IsValidXpi(cachedPath))
            return (cachedPath, errors);

        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            try
            {
                AuditLog.Write($"Firefox XPI prefetch started — {downloadUrl}");
                var bytes = Http.GetByteArrayAsync(downloadUrl).GetAwaiter().GetResult();
                if (bytes.Length < 4096)
                {
                    errors.Add($"Downloaded XPI is too small ({bytes.Length} bytes).");
                }
                else
                {
                    File.WriteAllBytes(cachedPath, bytes);
                    AuditLog.Write($"Firefox XPI prefetched — {bytes.Length} bytes cached at {cachedPath}.");
                    return (cachedPath, errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Firefox XPI download failed: {ex.Message}");
                AuditLog.Write($"Firefox XPI prefetch failed: {ex}");
            }
        }

        var fallback = FindLocalFallback(version, addonId);
        if (fallback is not null)
        {
            AuditLog.Write(
                $"Firefox XPI using local fallback — {fallback} (remote URL unavailable; target v{version}).");
            return (fallback, errors);
        }

        if (errors.Count == 0)
            errors.Add("No local Firefox XPI available and download URL is missing.");

        return (null, errors);
    }

    private static string? FindLocalFallback(string version, string addonId)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var exact = Path.Combine(CacheDir, $"guardi-image-shield-{version.Replace('.', '_')}.xpi");
        if (IsValidXpi(exact))
            return exact;

        foreach (var root in FirefoxInstallRoots.All())
        {
            var bundled = Path.Combine(root, "distribution", "extensions", addonId + ".xpi");
            if (!IsValidXpi(bundled))
                continue;

            var bundledVersion = ReadXpiManifestVersion(bundled);
            if (bundledVersion is not null
                && ExtensionPresenceProbe.CompareVersions(bundledVersion, version) >= 0)
            {
                return bundled;
            }
        }

        // Never reuse an older cached XPI when Guardi targets a newer release.
        return null;
    }

    private static string? ReadXpiManifestVersion(string xpiPath)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(xpiPath);
            var entry = zip.GetEntry("manifest.json");
            if (entry is null)
                return null;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            using var doc = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidXpi(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && new FileInfo(path).Length > 4096;
}
