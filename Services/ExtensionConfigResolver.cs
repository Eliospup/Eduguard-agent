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
        var chromiumOk = !Config.ExtensionGuardEnforceChromium || ChromiumInstallResolver.IsInstallReady(cfg);
        var firefoxOk = !Config.ExtensionGuardEnforceFirefox
            || cfg.IsFirefoxStoreReady
            || (Config.ExtensionGuardFirefoxLocalMode && FirefoxLocalPackager.HasSource()
                && (FirefoxEditionHelper.CanInstallLocalUnsigned || FirefoxEditionHelper.IsReleaseInstalled()));

        return chromiumOk && firefoxOk;
    }
}
