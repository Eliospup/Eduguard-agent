using System.IO.Compression;
using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

/// <summary>
/// Picks install strategy per browser. Chromium uses Chrome Web Store policy or
/// --load-extension sideload; Firefox uses enterprise policy / Dev Edition.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ExtensionInstallRouter
{
    public static ExtensionInstallMethod Resolve(ProtectedBrowser browser, ExtensionRuntimeConfig? cfg)
    {
        if (!browser.IsInstalled())
            return ExtensionInstallMethod.NotApplicable;

        // Microsoft Edge and Brave are blocked: neither reliably force-installs the shield from
        // the Chrome Web Store on a home PC — Edge refuses off-Edge-store force-install on
        // non-domain machines, and Brave loads the policy but never actually installs the CRX
        // (known Brave limitation). Guardi keeps no listing on their own stores, so they can
        // never carry the shield — close them on sight (before the per-browser toggle) and steer
        // the child to Chrome/Firefox. Dev/unpacked sideload still works for maintainers, so only
        // block in the real (Web Store) mode.
        if ((browser.Kind == BrowserKind.Edge || browser.Kind == BrowserKind.Brave)
            && Config.ExtensionGuardEnforceChromium
            && !ChromiumUnpackedMode.IsActive)
        {
            return ExtensionInstallMethod.ChromiumStoreBlocked;
        }

        // Hard block: while the Chromium extension isn't published on the Web Store, Guardi
        // cannot protect Chrome — close it on sight, regardless of any per-mode or per-browser
        // toggle. This gate is intentionally BEFORE ShouldEnforceBrowser so a disabled toggle
        // can't leave a Chromium browser open.
        if (browser.Engine == BrowserEngine.Chromium
            && Config.ExtensionGuardEnforceChromium
            && !Config.ChromiumExtensionPublished
            && !ChromiumUnpackedMode.IsActive)
        {
            return ExtensionInstallMethod.ChromiumStoreBlocked;
        }

        if (!ImageShieldPolicy.ShouldEnforceBrowser(browser.Kind))
            return ExtensionInstallMethod.NotApplicable;

        if (browser.Engine == BrowserEngine.Chromium)
            return ResolveChromium(cfg);

        if (browser.Engine == BrowserEngine.Gecko)
            return ResolveFirefox(cfg);

        return ExtensionInstallMethod.NotApplicable;
    }

    private static ExtensionInstallMethod ResolveChromium(ExtensionRuntimeConfig? cfg)
    {
        if (!Config.ExtensionGuardEnforceChromium)
            return ExtensionInstallMethod.NotApplicable;

        if (ChromiumUnpackedMode.IsActive)
            return ExtensionInstallMethod.ChromiumUnpackedSideload;

        return cfg?.IsChromiumReady == true
            ? ExtensionInstallMethod.ChromiumWebStore
            : ExtensionInstallMethod.ChromiumStoreBlocked;
    }

    private static ExtensionInstallMethod ResolveFirefox(ExtensionRuntimeConfig? cfg)
    {
        if (!Config.ExtensionGuardEnforceFirefox)
            return ExtensionInstallMethod.NotApplicable;

        if (Config.ExtensionGuardFirefoxLocalMode && FirefoxLocalPackager.HasSource())
        {
            if (FirefoxEditionHelper.CanInstallLocalUnsigned)
                return ExtensionInstallMethod.FirefoxLocalUnsigned;

            return ExtensionInstallMethod.FirefoxReleaseBlocked;
        }

        if (cfg?.IsFirefoxStoreReady == true)
            return ExtensionInstallMethod.FirefoxSignedEnterprise;

        var xpiPath = FindLocalSignedXpi();
        if (xpiPath is not null && IsMozillaSignedXpi(xpiPath))
            return ExtensionInstallMethod.FirefoxSignedEnterprise;
        if (FirefoxEditionHelper.IsDeveloperEditionInstalled())
            return ExtensionInstallMethod.FirefoxDeveloperEdition;

        return ExtensionInstallMethod.FirefoxReleaseBlocked;
    }

    private static string? FindLocalSignedXpi()
    {
        var source = ExtensionBundleLocator.FindExtensionSourceRoot();
        if (source is null)
            return null;

        var candidates = new[]
        {
            Path.Combine(source, "web-ext-output", "guardi_image_shield-0.6.9.xpi"),
            Path.Combine(source, "web-ext-output", "guardi-image-shield.xpi"),
            Path.Combine(source, "dist", "signed.xpi"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        var outputDir = Path.Combine(source, "web-ext-output");
        if (!Directory.Exists(outputDir))
            return null;

        return Directory.EnumerateFiles(outputDir, "*.xpi").FirstOrDefault();
    }

    public static bool IsMozillaSignedXpi(string xpiPath)
    {
        if (!File.Exists(xpiPath))
            return false;

        try
        {
            using var zip = ZipFile.OpenRead(xpiPath);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.StartsWith("META-INF/mozilla", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static bool IsFirefoxDeveloperEditionInstalled() =>
        FirefoxEditionHelper.IsDeveloperEditionInstalled();

    public static string Describe(ExtensionInstallMethod method) => method switch
    {
        ExtensionInstallMethod.ChromiumWebStore =>
            "Chromium — force-install from Chrome Web Store",
        ExtensionInstallMethod.ChromiumUnpackedSideload =>
            "Chromium — unpacked sideload via --load-extension",
        ExtensionInstallMethod.FirefoxSignedEnterprise =>
            "Firefox — enterprise policy with Mozilla-signed XPI",
        ExtensionInstallMethod.FirefoxLocalUnsigned =>
            "Firefox Developer Edition — local unsigned XPI via enterprise policy",
        ExtensionInstallMethod.FirefoxDeveloperEdition =>
            "Firefox Developer Edition — dev policy install",
        ExtensionInstallMethod.FirefoxReleaseBlocked =>
            "Firefox Release — unsigned extensions blocked by Mozilla (sign on AMO or install Firefox Developer Edition)",
        ExtensionInstallMethod.ChromiumStoreBlocked =>
            "Chromium — extension not yet available on the Chrome Web Store (browser blocked)",
        _ => "not applicable",
    };
}
