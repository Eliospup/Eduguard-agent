namespace EduGuardAgent.Services;

/// <summary>Resolves Chromium extension ID/URL from store-config.json only.</summary>
internal static class ChromiumInstallResolver
{
    public static ExtensionRuntimeConfig Resolve(ExtensionRuntimeConfig cfg) => cfg;

    public static bool IsInstallReady(ExtensionRuntimeConfig cfg)
    {
        if (!Config.ExtensionGuardEnforceChromium)
            return true;

        if (ChromiumUnpackedMode.IsActive)
            return ChromiumUnpackedDeployer.HasSource();

        return cfg.IsChromiumReady;
    }
}
