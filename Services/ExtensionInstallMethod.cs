namespace EduGuardAgent.Services;

/// <summary>How Guardi installs the shield for a given browser on this machine.</summary>
internal enum ExtensionInstallMethod
{
    /// <summary>Chromium force-install via Chrome Web Store update URL (works on personal Windows).</summary>
    ChromiumWebStore,

    /// <summary>Chromium — load unpacked build via --load-extension (personal Windows dev).</summary>
    ChromiumUnpackedSideload,

    /// <summary>Firefox enterprise policy with a Mozilla-signed XPI URL.</summary>
    FirefoxSignedEnterprise,

    /// <summary>Firefox Release — unsigned local XPI bundled via policies.json.</summary>
    FirefoxLocalUnsigned,

    /// <summary>Firefox Developer Edition — unsigned dev XPI via policy.</summary>
    FirefoxDeveloperEdition,

    /// <summary>Standard Firefox cannot load our unsigned extension — block the browser.</summary>
    FirefoxReleaseBlocked,

    /// <summary>Chromium extension not yet available on the Chrome Web Store — block the browser.</summary>
    ChromiumStoreBlocked,

    /// <summary>Browser not installed or enforcement disabled for this engine.</summary>
    NotApplicable,
}
