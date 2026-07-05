namespace EduGuardAgent.Services;

/// <summary>Resolves extension IDs/URLs from store-config.json or Config.cs fallback.</summary>
internal static class ExtensionConfigResolver
{
    public static ExtensionRuntimeConfig? Active
    {
        get
        {
            ExtensionRuntimeConfig? resolved = null;

            if (ExtensionRuntime.Current is { } current && IsReadyWith(current))
                resolved = current;
            else
            {
                var fallback = ExtensionRuntimeConfig.FromConfigFallback();
                resolved = IsReadyWith(fallback) ? fallback : ExtensionRuntime.Current ?? fallback;
            }

            return ChromiumInstallResolver.Resolve(resolved ?? ExtensionRuntimeConfig.FromConfigFallback());
        }
    }

    public static bool IsReady => Active is not null && IsReadyWith(Active);

    public static bool IsReadyWith(ExtensionRuntimeConfig cfg)
    {
        // Chromium readiness no longer gates the whole guard. When the extension is not on
        // the Web Store yet, Chromium browsers are actively blocked (ChromiumStoreBlocked)
        // inside ExtensionEnforcementService — the guard still needs to run for Firefox.
        var firefoxOk = !Config.ExtensionGuardEnforceFirefox
            || cfg.IsFirefoxStoreReady
            || (Config.ExtensionGuardFirefoxLocalMode && FirefoxLocalPackager.HasSource()
                && (FirefoxEditionHelper.CanInstallLocalUnsigned || FirefoxEditionHelper.IsReleaseInstalled()));

        return firefoxOk;
    }
}
