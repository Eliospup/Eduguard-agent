using EduGuardAgent.Security;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Services;

/// <summary>Loads <c>extension/store-config.json</c> at startup.</summary>
internal static class ExtensionStoreConfigLoader
{
    public static string? LastError { get; private set; }

    public static bool Initialize(Action<string>? log = null)
    {
        LastError = null;

        var path = FindStoreConfigPath();
        ExtensionRuntimeConfig? cfg;

        if (path is null)
        {
            cfg = ExtensionRuntimeConfig.FromConfigFallback();
            ExtensionRuntime.Current = cfg;
        }
        else
        {
            cfg = ExtensionRuntimeConfig.TryLoadFromFile(path);
            if (cfg is null)
            {
                LastError = "extension/store-config.json is invalid JSON.";
                Note(LastError, log);
                ExtensionRuntime.Current = null;
                return false;
            }

            ExtensionRuntime.Current = cfg;
        }

        if (ExtensionConfigResolver.IsReadyWith(cfg))
        {
            var mode = DescribeActiveMode(cfg);
            AuditLog.Write($"Image shield config ready — {mode} (store-config: {path ?? "Config.cs fallback"}).");
            log?.Invoke($"{UiCopy.MascotName} will force-install the image shield ({mode}).");
            LogChromiumStorePreflight(cfg, log);
            LogFirefoxStorePreflight(cfg);
            return true;
        }

        LastError = BuildNotReadyMessage(cfg);
        Note(LastError, log);
        return false;
    }

    private static void LogChromiumStorePreflight(ExtensionRuntimeConfig cfg, Action<string>? log)
    {
        if (!Config.ExtensionGuardEnforceChromium
            || ChromiumUnpackedMode.IsActive
            || !cfg.IsChromiumReady)
        {
            return;
        }

        var probe = ChromiumWebStoreProbe.Check(cfg.ChromiumExtensionId);
        switch (probe.Status)
        {
            case ChromiumWebStoreListingStatus.Listed:
                AuditLog.Write(
                    $"Chromium extension preflight OK — {cfg.ChromiumExtensionId}" +
                    (probe.StoreVersion is { } v ? $" v{v}" : "") +
                    $" ({(cfg.IsChromiumSelfHosted ? "self-hosted CRX" : "Web Store")}).");
                break;
            case ChromiumWebStoreListingStatus.NotListed:
                AuditLog.Write(
                    $"Chromium extension preflight: {cfg.ChromiumExtensionId} not available yet ({probe.Detail ?? "noupdate"}).");
                log?.Invoke(cfg.IsChromiumSelfHosted
                    ? "Chrome policy is ready, but the self-hosted CRX/updates.xml isn't reachable yet — Chrome won't be blocked."
                    : "Chrome policy is ready, but the extension isn't on the Chrome Web Store yet — Chrome won't be blocked.");
                break;
            default:
                AuditLog.Write(
                    $"Chrome Web Store preflight inconclusive for {cfg.ChromiumExtensionId} ({probe.Detail ?? "unknown"}).");
                break;
        }
    }

    private static string BuildNotReadyMessage(ExtensionRuntimeConfig cfg)
    {
        var parts = new List<string>();

        if (Config.ExtensionGuardEnforceChromium && !cfg.IsChromiumReady
            && !ChromiumUnpackedMode.IsActive)
        {
            parts.Add(
                "Chrome Web Store ID missing in store-config.json (set ExtensionGuardEnforceChromium=false while review is pending).");
        }

        if (Config.ExtensionGuardEnforceFirefox
            && !cfg.IsFirefoxStoreReady
            && !(Config.ExtensionGuardFirefoxLocalMode && FirefoxLocalPackager.HasSource()))
        {
            parts.Add(
                "Firefox extension not built. Run: cd extension && npm.cmd run build:firefox");
        }
        else if (Config.ExtensionGuardEnforceFirefox
            && Config.ExtensionGuardFirefoxLocalMode
            && FirefoxLocalPackager.HasSource()
            && !FirefoxEditionHelper.CanInstallLocalUnsigned
            && !cfg.IsFirefoxStoreReady)
        {
            parts.Add(
                "Firefox Release cannot load unsigned extensions. Sign on AMO or install Firefox Developer Edition.");
        }

        return parts.Count > 0
            ? string.Join(" ", parts)
            : "Image shield configuration is incomplete.";
    }

    private static void LogFirefoxStorePreflight(ExtensionRuntimeConfig cfg)
    {
        if (!Config.ExtensionGuardEnforceFirefox || !cfg.IsFirefoxStoreReady)
            return;

        var (local, errors) = FirefoxSignedXpiCache.EnsureCached(
            cfg.FirefoxInstallUrl,
            cfg.Version,
            cfg.FirefoxAddonId);
        if (local is not null)
        {
            if (errors.Count > 0)
            {
                AuditLog.Write(
                    $"Firefox XPI preflight recovered via local fallback for v{cfg.Version} " +
                    $"(check extension/store-config.json — remote URL failed).");
            }

            return;
        }

        AuditLog.Write(
            $"Firefox XPI preflight failed for v{cfg.Version} — no local bundle/cache and remote URL unavailable. " +
            $"{string.Join("; ", errors)}");
    }

    private static string DescribeActiveMode(ExtensionRuntimeConfig cfg)
    {
        var modes = new List<string>();

        if (Config.ExtensionGuardEnforceChromium)
        {
            if (ChromiumUnpackedMode.IsActive)
                modes.Add("Chromium unpacked (--load-extension)");
            else if (cfg.IsChromiumReady)
                modes.Add($"Chrome {cfg.ChromiumExtensionId} ({(cfg.IsChromiumSelfHosted ? "self-hosted CRX" : "Web Store")})");
        }

        if (Config.ExtensionGuardEnforceFirefox)
        {
            if (cfg.IsFirefoxStoreReady)
                modes.Add("Firefox signed XPI");
            else if (Config.ExtensionGuardFirefoxLocalMode)
                modes.Add(FirefoxEditionHelper.IsDeveloperEditionInstalled()
                    ? "Firefox Developer Edition local"
                    : "Firefox local (Dev Edition missing)");
        }

        return modes.Count > 0 ? string.Join(", ", modes) : "policies pending";
    }

    private static string? FindStoreConfigPath()
    {
        var candidates = new List<string>();

        var source = ExtensionBundleLocator.FindExtensionSourceRoot();
        if (source is not null)
            candidates.Add(Path.Combine(source, "store-config.json"));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "extension", "store-config.json"));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "extension", "store-config.json")));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static void Note(string message, Action<string>? log)
    {
        AuditLog.Write($"Image shield not configured: {message}");
        log?.Invoke(message);
    }
}
