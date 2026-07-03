using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal enum ChromiumWebStoreListingStatus
{
    Unknown,
    Listed,
    NotListed,
}

internal readonly record struct ChromiumWebStoreProbeResult(
    ChromiumWebStoreListingStatus Status,
    string? StoreVersion,
    string? Detail);

/// <summary>
/// Preflight check: is the extension ID actually served by its configured update endpoint?
/// Works for both the Chrome Web Store (POST gupdate protocol) and a self-hosted static
/// <c>updates.xml</c> (plain GET, e.g. a GitHub release asset). Avoids closing Chrome when
/// the policy is set but the update source has nothing to install yet.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ChromiumWebStoreProbe
{
    private const string WebStoreUpdateUrl = ExtensionRuntimeConfig.ChromeWebStoreUpdateUrl;

    /// <summary>The Chrome Web Store speaks the update2 POST protocol; self-hosted manifests are static GETs.</summary>
    private static bool IsWebStore(string updateUrl) =>
        updateUrl.Contains("clients2.google.com", StringComparison.OrdinalIgnoreCase)
        || updateUrl.Contains("update2/crx", StringComparison.OrdinalIgnoreCase);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly object Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan ListedCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NotListedCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UnknownCacheTtl = TimeSpan.FromMinutes(2);

    public static ChromiumWebStoreProbeResult Check(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId)
            || extensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
        {
            return new ChromiumWebStoreProbeResult(
                ChromiumWebStoreListingStatus.NotListed,
                null,
                "extension id not configured");
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(extensionId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
                return cached.Result;
        }

        var result = ProbeRemote(extensionId);

        lock (Gate)
        {
            var ttl = result.Status switch
            {
                ChromiumWebStoreListingStatus.Listed => ListedCacheTtl,
                ChromiumWebStoreListingStatus.NotListed => NotListedCacheTtl,
                _ => UnknownCacheTtl,
            };
            Cache[extensionId] = new CacheEntry(result, DateTimeOffset.UtcNow.Add(ttl));
        }

        return result;
    }

    public static void Invalidate(string extensionId)
    {
        lock (Gate)
            Cache.Remove(extensionId);
    }

    private static ChromiumWebStoreProbeResult ProbeRemote(string extensionId)
    {
        var updateUrl = ExtensionRuntime.Current?.ChromeUpdateUrl ?? Config.ImageShieldChromeUpdateUrl;
        if (string.IsNullOrWhiteSpace(updateUrl))
            updateUrl = WebStoreUpdateUrl;

        try
        {
            using var response = IsWebStore(updateUrl)
                ? PostWebStore(updateUrl, extensionId)
                : Http.GetAsync(updateUrl).GetAwaiter().GetResult();

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                AuditLog.Write(
                    $"Chromium extension preflight HTTP {(int)response.StatusCode} for {extensionId} ({updateUrl}).");
                return new ChromiumWebStoreProbeResult(
                    ChromiumWebStoreListingStatus.Unknown,
                    null,
                    $"http {(int)response.StatusCode}");
            }

            return ParseResponse(body);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Chromium extension preflight failed for {extensionId} ({updateUrl}): {ex.Message}");
            return new ChromiumWebStoreProbeResult(
                ChromiumWebStoreListingStatus.Unknown,
                null,
                ex.Message);
        }
    }

    private static HttpResponseMessage PostWebStore(string updateUrl, string extensionId)
    {
        var xml = BuildRequestXml(extensionId);
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        return Http.PostAsync(updateUrl, content).GetAwaiter().GetResult();
    }

    private static string BuildRequestXml(string extensionId) =>
        $"""
        <?xml version='1.0' encoding='UTF-8'?>
        <request xmlns='http://www.google.com/update2/request' protocol='2.0'>
          <os platform='win' arch='x64' version='10.0'/>
          <app appid='{extensionId}' version='0.0.0.0'/>
        </request>
        """;

    private static ChromiumWebStoreProbeResult ParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChromiumWebStoreProbeResult(
                ChromiumWebStoreListingStatus.Unknown,
                null,
                "empty response");
        }

        try
        {
            var doc = XDocument.Parse(body);
            var update = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("updatecheck", StringComparison.OrdinalIgnoreCase));

            if (update is null)
            {
                return new ChromiumWebStoreProbeResult(
                    ChromiumWebStoreListingStatus.NotListed,
                    null,
                    "no updatecheck node");
            }

            var status = (string?)update.Attribute("status") ?? "";
            var version = (string?)update.Attribute("version");
            var codebase = (string?)update.Attribute("codebase");

            if (status.Equals("ok", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(codebase))
            {
                return new ChromiumWebStoreProbeResult(
                    ChromiumWebStoreListingStatus.Listed,
                    version,
                    status);
            }

            if (status.Equals("noupdate", StringComparison.OrdinalIgnoreCase)
                || status.Equals("error", StringComparison.OrdinalIgnoreCase)
                || status.Equals("-", StringComparison.OrdinalIgnoreCase))
            {
                return new ChromiumWebStoreProbeResult(
                    ChromiumWebStoreListingStatus.NotListed,
                    version,
                    status);
            }

            return new ChromiumWebStoreProbeResult(
                ChromiumWebStoreListingStatus.Unknown,
                version,
                string.IsNullOrWhiteSpace(status) ? "unrecognized updatecheck" : status);
        }
        catch (Exception ex)
        {
            return new ChromiumWebStoreProbeResult(
                ChromiumWebStoreListingStatus.Unknown,
                null,
                $"parse error: {ex.Message}");
        }
    }

    private sealed record CacheEntry(ChromiumWebStoreProbeResult Result, DateTimeOffset ExpiresAt);
}
