using System.Text.Json;

namespace EduGuardAgent.Services;

/// <summary>
/// Extension IDs and update URLs from <c>extension/store-config.json</c> after the
/// extension is published on the Chrome Web Store and Firefox AMO.
/// </summary>
internal sealed record ExtensionRuntimeConfig(
    string ChromiumExtensionId,
    string ChromeUpdateUrl,
    string FirefoxAddonId,
    string FirefoxInstallUrl,
    string Version)
{
    public const string ChromeWebStoreUpdateUrl = "https://clients2.google.com/service/update2/crx";

    /// <summary>
    /// Ready when the extension ID is set AND the update source is the Chrome Web Store over
    /// HTTPS. A localhost/dev URL is treated as not-ready so we never write a policy pointing
    /// at a machine-local server.
    /// </summary>
    public bool IsChromiumReady =>
        !string.IsNullOrWhiteSpace(ChromiumExtensionId)
        && !ChromiumExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(ChromeUpdateUrl)
        && ChromeUpdateUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && !ChromeUpdateUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
        && !ChromeUpdateUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase);

    public bool IsFirefoxStoreReady =>
        !string.IsNullOrWhiteSpace(FirefoxInstallUrl)
        && !FirefoxInstallUrl.StartsWith("REPLACE_", StringComparison.Ordinal)
        && FirefoxInstallUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && !FirefoxInstallUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

    /// <summary>Legacy check — both store channels configured.</summary>
    public bool IsStoreReady => IsChromiumReady && IsFirefoxStoreReady;

    public bool IsFirefoxSigned => IsFirefoxStoreReady;

    public static ExtensionRuntimeConfig? TryLoadFromFile(string configPath)
    {
        if (!File.Exists(configPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            var id = root.GetProperty("chromiumExtensionId").GetString() ?? "";
            var chromeUrl = root.TryGetProperty("chromeUpdateUrl", out var cu)
                ? cu.GetString() ?? ChromeWebStoreUpdateUrl
                : ChromeWebStoreUpdateUrl;
            var firefoxId = root.TryGetProperty("firefoxAddonId", out var fa)
                ? fa.GetString() ?? Config.ImageShieldFirefoxAddonId
                : Config.ImageShieldFirefoxAddonId;
            var firefoxUrl = root.GetProperty("firefoxInstallUrl").GetString() ?? "";
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

            if (!chromeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                chromeUrl = ChromeWebStoreUpdateUrl;

            return new ExtensionRuntimeConfig(id, chromeUrl, firefoxId, firefoxUrl, version);
        }
        catch
        {
            return null;
        }
    }

    public static ExtensionRuntimeConfig FromConfigFallback() =>
        new(
            Config.ImageShieldExtensionId,
            Config.ImageShieldChromeUpdateUrl,
            Config.ImageShieldFirefoxAddonId,
            Config.ImageShieldFirefoxInstallUrl,
            Version: "");
}

/// <summary>Singleton holder set at startup from store-config.json.</summary>
internal static class ExtensionRuntime
{
    public static ExtensionRuntimeConfig? Current { get; set; }
}
