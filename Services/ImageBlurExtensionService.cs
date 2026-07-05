using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;



namespace EduGuardAgent.Services;



internal sealed class ImageBlurExtensionService : IDisposable

{

    private readonly ImageShieldBackupStore _backupStore = new();

    private readonly ChromiumExtensionPolicy _chromium = new();

    private readonly FirefoxExtensionPolicy _firefox = new();

    private FirefoxDeployOutcome? _lastFirefoxDeployOutcome;
    private ChromiumDeployOutcome? _lastChromiumDeployOutcome;
    private bool _firefoxStartupSyncDone;
    private bool _firefoxStoreStartupSyncDone;
    private bool _chromiumStartupSyncDone;

    public FirefoxDeployOutcome? LastFirefoxDeployOutcome => _lastFirefoxDeployOutcome;



    public bool IsActive { get; private set; }

    /// <summary>Whether runtime filtering is enabled (distinct from install persistence).</summary>
    public bool RuntimeFilteringActive { get; private set; }

    public string? LastError { get; private set; }

    public bool HasAdminRights => HostsFileManager.IsRunningAsAdmin();

    public bool IsConfigured => ExtensionConfigResolver.IsReady;

    public bool HasPersistedInstall => _backupStore.Load() is not null;

    public bool ApplyPoliciesOnly(ImageShieldSettings? settings = null)

    {

        if (!HasAdminRights)

        {

            LastError = "Administrator rights are required to force-install the image shield.";

            IsActive = false;

            AuditLog.Write(LastError);

            return false;

        }



        var cfg = ExtensionConfigResolver.Active;

        if (cfg is null || !IsConfigured)

        {

            LastError = ExtensionStoreConfigLoader.LastError

                ?? "Image shield is not configured.";

            IsActive = false;

            AuditLog.Write($"Image shield not applied: {LastError}");

            return false;

        }



        return ApplyPoliciesCore(cfg, settings ?? ImageShieldSettings.Default);

    }



    public bool Apply(ImageShieldSettings? settings = null) => ApplyPoliciesOnly(settings);



    public bool EnsureBrowser(ProtectedBrowser browser)

    {

        if (!HasAdminRights || !IsConfigured)

            return false;



        var cfg = ExtensionConfigResolver.Active!;

        var method = ExtensionInstallRouter.Resolve(browser, cfg);



        if (method == ExtensionInstallMethod.NotApplicable

            || method == ExtensionInstallMethod.FirefoxReleaseBlocked)

            return false;



        if (!IsActive && !ApplyPoliciesCore(cfg, ImageShieldSettings.Default))

            return false;

        // The ExtensionSettings.private_browsing policy only sets the initial value — Firefox
        // leaves the per-add-on "Run in Private Windows" toggle fully user-editable, so a
        // supervised user can flip it back off. Re-grant it on every enforcement tick.
        if (browser.Kind == BrowserKind.Firefox)
            FirefoxPrivateBrowsingEnforcer.EnsureGranted(cfg.FirefoxAddonId);

        return method switch

        {

            ExtensionInstallMethod.ChromiumWebStore => true,

            ExtensionInstallMethod.ChromiumUnpackedSideload => EnsureChromiumUnpackedPolicy(cfg),

            ExtensionInstallMethod.FirefoxSignedEnterprise => EnsureFirefoxSignedPolicy(cfg),

            ExtensionInstallMethod.FirefoxLocalUnsigned => EnsureFirefoxLocalPolicy(cfg),

            ExtensionInstallMethod.FirefoxDeveloperEdition => EnsureFirefoxDevPolicy(cfg),

            _ => false,

        };

    }



    public void Reassert()

    {

        if (!IsActive || !HasAdminRights)

            return;



        var cfg = ExtensionConfigResolver.Active;

        if (cfg is null || !IsConfigured)

            return;



        ApplyPoliciesCore(cfg, ImageShieldSettings.Default);

    }

    /// <summary>
    /// Turns filtering/UI on or off via managed storage while keeping the extension
    /// installed. Used when Guardi exits or Dom disables image shield from the web.
    /// </summary>
    public void SetRuntimeActive(bool active, ImageShieldSettings? tuning = null)

    {

        ImageShieldRuntimeStore.SetFilteringActive(active);
        RuntimeFilteringActive = active;

        if (!HasAdminRights)

        {

            LastError = "Administrator rights are required to update image shield runtime policies.";

            AuditLog.Write(

                active

                    ? "Image shield runtime active requested but admin rights are missing."

                    : "Image shield runtime inactive — filtering disabled (policy update skipped: not admin).");

            return;

        }

        var cfg = ExtensionConfigResolver.Active;

        if (cfg is null || !IsConfigured)

        {

            AuditLog.Write("Image shield runtime flag updated in memory only — extension not configured.");

            return;

        }



        var settings = (tuning ?? ImageShieldSettings.Default) with { ShieldActive = active };



        if (active && !HasPersistedInstall)

        {

            ApplyPoliciesCore(cfg, settings);

            return;

        }



        if (!HasPersistedInstall)

        {

            AuditLog.Write("Image shield runtime flag updated — no persisted browser install to patch.");

            return;

        }



        UpdateRuntimePoliciesOnly(cfg, settings);

        if (active)
            RefreshFirefoxExtensionInstall(cfg, settings);

        IsActive = HasPersistedInstall;

        LastError = null;

        AuditLog.Write(active

            ? "Image shield runtime active — extension filtering enabled."

            : "Image shield runtime inactive — extension stays installed, no filtering.");

    }



    public void Release()

    {

        var state = _backupStore.Load();

        if (state is null)

        {

            RuntimeFilteringActive = false;
            ImageShieldRuntimeStore.SetFilteringActive(false);
            IsActive = false;

            LastError = null;

            return;

        }



        var hadFirefox = state.FirefoxAddonIds.Count > 0

            || state.FirefoxBundledXpiPaths.Count > 0

            || state.FirefoxProfileUnpackedDirs.Count > 0;

        var hadChromium = state.RegistryValues.Count > 0

            || state.ChromiumProfileUnpackedDirs.Count > 0;



        var errors = _chromium.Restore(state.RegistryValues, state.RegistryKeysCreated);

        foreach (var addonId in state.FirefoxAddonIds)

            errors.AddRange(_firefox.Restore(addonId));



        foreach (var bundledPath in state.FirefoxBundledXpiPaths)

            errors.AddRange(FirefoxExtensionPolicy.RestoreBundledXpi(bundledPath));



        foreach (var unpackedDir in state.FirefoxProfileUnpackedDirs)

            errors.AddRange(FirefoxProfileExtensionInstaller.RemoveUnpacked(unpackedDir));



        if (state.FirefoxAddonIds.Count > 0)
        {
            foreach (var addonId in state.FirefoxAddonIds)
            {
                var (profilesTouched, purgeErrors) = FirefoxProfileExtensionInstaller.PurgeAddonFromAllProfiles(addonId);
                if (profilesTouched > 0)
                    AuditLog.Write($"Firefox profile purge — removed {addonId} from {profilesTouched} profile(s).");
                errors.AddRange(purgeErrors);
            }
        }



        if (errors.Count == 0)

        {

            _backupStore.Clear();

            RuntimeFilteringActive = false;
            ImageShieldRuntimeStore.SetFilteringActive(false);
            IsActive = false;

            LastError = null;

            AuditLog.Write("Image shield released — extension uninstalled, browser policies restored.");

            CloseBrowsersAfterRelease(hadFirefox, hadChromium);

            return;

        }



        RuntimeFilteringActive = false;
        ImageShieldRuntimeStore.SetFilteringActive(false);
        IsActive = false;

        LastError = string.Join("; ", errors);

        AuditLog.Write($"Image shield release incomplete — will retry on next startup: {LastError}");

        CloseBrowsersAfterRelease(hadFirefox, hadChromium);

    }



    private static void CloseBrowsersAfterRelease(bool hadFirefox, bool hadChromium)
    {
        // Guardi Image Shield handles its own "offline" state by polling the local HTTP server.
        // When Guardi exits, the extension instantly detects the connection drop and gracefully
        // disables all its filters (SafeSearch, Image blur, URL blocks) without needing to be
        // forcefully uninstalled mid-session.
        // Force-closing the user's browser during shutdown is therefore unnecessary and frustrating.
        // The enterprise policies are removed from disk/registry during Release(), so the extension
        // will naturally disappear the next time the user restarts their browser.
        AuditLog.Write("Image shield released — browser policies removed. Browsers left running (extension gracefully sleeps).");
    }



    public void Dispose()
    {
        // Install persists across Guardi sessions — do not Release() here.
    }



    public void TryReleaseOrphanedState() => ReleaseOrphanedState();

    /// <summary>
    /// Cleans dev-era Chromium policies (localhost sources, stale forcelist) before apply.
    /// Safe to call multiple times per session.
    /// </summary>
    public void TryMigrateStalePolicies()
    {
        if (!HasAdminRights)
            return;

        var cfg = ExtensionConfigResolver.Active;
        ChromiumExtensionPolicy.TryMigrateStaleDevPolicies(
            cfg?.ChromiumExtensionId,
            cfg?.ChromeUpdateUrl);
    }

    /// <summary>
    /// Keeps Firefox enterprise install_url aligned with store-config (new GitHub release XPI).
    /// </summary>
    public bool TrySyncFirefoxStoreOnStartup(
        Action<string>? log = null,
        Action<string, string>? onRestarting = null)
    {
        if (!HasAdminRights)
        {
            AuditLog.Write(
                "Firefox extension startup sync skipped — run Guardi as administrator to install policies.");
            log?.Invoke(
                $"{UiCopy.MascotName} needs administrator rights to install the Firefox shield.");
            return false;
        }

        if (!IsConfigured)
            return false;

        var cfg = ExtensionConfigResolver.Active;
        if (cfg is null || !cfg.IsFirefoxStoreReady || Config.ExtensionGuardFirefoxLocalMode)
            return false;

        if (_firefoxStoreStartupSyncDone && !FirefoxExtensionDeployStore.NeedsDeploy(cfg.Version))
            return false;

        _firefoxStoreStartupSyncDone = true;

        return TryRestartFirefoxForShield(
            ResolveFirefoxDeploySettings(),
            log,
            onRestarting,
            enforceBrowserPolicy: false);
    }

    /// <summary>
    /// Re-downloads the signed XPI, refreshes enterprise policy, and purges stale profile copies.
    /// Called when the extension guard detects a version mismatch.
    /// </summary>
    public bool PrepareFirefoxExtensionUpgrade(Action<string>? log = null)
    {
        if (!HasAdminRights || !IsConfigured || Config.ExtensionGuardFirefoxLocalMode)
            return false;

        var cfg = ExtensionConfigResolver.Active;
        if (cfg is null || !cfg.IsFirefoxStoreReady)
            return false;

        if (!FirefoxExtensionDeployStore.NeedsDeploy(cfg.Version))
        {
            var firefox = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);
            if (firefox is null)
                return false;

            var profile = FirefoxExtensionInstallState.ProbeProfileInstall(firefox, cfg.FirefoxAddonId);
            var beat = ExtensionHeartbeatHub.Get(BrowserKind.Firefox);
            var runtime = beat?.Version ?? profile.Version;
            if (runtime is null
                || ExtensionPresenceProbe.CompareVersions(runtime, cfg.Version) >= 0)
            {
                return false;
            }
        }

        if (!ApplyFirefoxSignedEnterprisePolicies(ResolveFirefoxDeploySettings(), requireDomToggle: false))
        {
            log?.Invoke(_imageShieldLastErrorOrDefault("Firefox upgrade policy could not be applied."));
            return false;
        }

        PurgeFirefoxAddonProfiles(cfg.FirefoxAddonId, $"upgrade to v{cfg.Version}");
        FirefoxExtensionDeployStore.MarkDeployed(cfg.Version);
        AuditLog.Write($"Firefox extension upgrade staged — target v{cfg.Version}.");
        log?.Invoke($"{UiCopy.MascotName} is updating the Firefox shield to version {cfg.Version}.");
        return true;
    }

    /// <summary>
    /// Same code path as the original 0.8.3 install: full ExtensionSettings force_installed policy.
    /// </summary>
    public bool ApplyFirefoxSignedEnterprisePolicies(
        ImageShieldSettings settings,
        bool requireDomToggle = true)
    {
        if (!HasAdminRights || !IsConfigured || Config.ExtensionGuardFirefoxLocalMode)
            return false;

        if (!Config.ExtensionGuardEnforceFirefox)
            return false;

        var cfg = ExtensionConfigResolver.Active;
        if (cfg is null || !cfg.IsFirefoxStoreReady)
            return false;

        if (requireDomToggle && !ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Firefox))
            return false;

        var firefox = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);
        if (firefox is null || !firefox.IsInstalled())
            return false;

        var firefoxErrors = new List<string>();
        var (firefoxApplied, bundledPaths, deployErrors) = DeployFirefoxSignedPolicy(cfg, settings);
        firefoxErrors.AddRange(deployErrors);

        if (bundledPaths.Count > 0)
        {
            AuditLog.Write(
                $"Firefox XPI bundled — {bundledPaths.Count} path(s) under distribution/extensions.");
        }
        else if (firefoxApplied.Count > 0)
        {
            AuditLog.Write(
                "Firefox XPI bundle skipped — install_url policy only (Firefox must download the XPI itself).");
        }

        if (firefoxErrors.Count > 0)
        {
            LastError = string.Join("; ", firefoxErrors);
            AuditLog.Write($"Firefox policy warnings: {LastError}");
        }

        if (firefoxApplied.Count == 0 && bundledPaths.Count == 0)
            return false;

        PersistFirefoxSignedBackup(cfg.FirefoxAddonId, bundledPaths);

        var installRef = bundledPaths.Count > 0
            ? FirefoxExtensionPolicy.ToFileInstallUrl(bundledPaths[0])
            : cfg.FirefoxInstallUrl;

        AuditLog.Write(
            $"Firefox extension policy applied (v{cfg.Version}) — policies: {firefoxApplied.Count}, " +
            $"bundled: {bundledPaths.Count}, url={installRef}.");

        IsActive = true;
        LastError = firefoxErrors.Count > 0 ? string.Join("; ", firefoxErrors) : null;
        return true;
    }

    private (List<string> PolicyPaths, List<string> BundledPaths, List<string> Errors) DeployFirefoxSignedPolicy(
        ExtensionRuntimeConfig cfg,
        ImageShieldSettings settings)
    {
        var errors = new List<string>();
        List<string> bundledPaths = [];

        var (cachedXpi, cacheErrors) = FirefoxSignedXpiCache.EnsureCached(cfg.FirefoxInstallUrl, cfg.Version);
        errors.AddRange(cacheErrors);
        if (cachedXpi is not null)
        {
            (bundledPaths, var bundleErrors) = _firefox.DeployBundledOnly(cfg.FirefoxAddonId, cachedXpi);
            errors.AddRange(bundleErrors);
        }

        List<string> policyPaths;
        List<string> policyErrors;
        if (bundledPaths.Count > 0)
        {
            (policyPaths, policyErrors) = _firefox.ApplyBundledSignedPolicies(cfg.FirefoxAddonId, settings);
        }
        else
        {
            (policyPaths, policyErrors) = _firefox.ApplyPolicies(
                cfg.FirefoxAddonId,
                cfg.FirefoxInstallUrl,
                xpiSourcePath: null,
                allowUnsigned: false);
            if (policyPaths.Count > 0)
                errors.AddRange(_firefox.UpdateRuntimeSettings(cfg.FirefoxAddonId, settings).Errors);
        }

        errors.AddRange(policyErrors);

        var installRef = bundledPaths.Count > 0
            ? FirefoxExtensionPolicy.ToFileInstallUrl(bundledPaths[0])
            : cfg.FirefoxInstallUrl;
        var (urlErrors, _) = _firefox.UpdateInstallUrl(cfg.FirefoxAddonId, installRef);
        errors.AddRange(urlErrors);

        if (policyPaths.Count > 0)
            errors.AddRange(_firefox.UpdateRuntimeSettings(cfg.FirefoxAddonId, settings).Errors);

        return (policyPaths, bundledPaths, errors);
    }

    private ImageShieldSettings ResolveFirefoxDeploySettings(ImageShieldSettings? settings = null)
    {
        var resolved = settings ?? ImageShieldSettings.Default;
        return resolved with { ShieldActive = ImageShieldRuntimeStore.IsFilteringActive };
    }

    private void PersistFirefoxSignedBackup(string addonId, List<string> bundledPaths)
    {
        var existing = _backupStore.Load() ?? new ImageShieldState();
        _backupStore.Save(new ImageShieldState
        {
            RegistryValues = existing.RegistryValues,
            RegistryKeysCreated = existing.RegistryKeysCreated,
            FirefoxAddonIds = [addonId],
            FirefoxBundledXpiPaths = bundledPaths.Count > 0 ? bundledPaths : existing.FirefoxBundledXpiPaths,
            FirefoxProfileUnpackedDirs = existing.FirefoxProfileUnpackedDirs,
            ChromiumProfileUnpackedDirs = existing.ChromiumProfileUnpackedDirs,
        });
    }

    /// <summary>
    /// Applies enterprise policy then restarts Firefox for a clean install or version upgrade.
    /// </summary>
    public bool TryRestartFirefoxForShield(
        ImageShieldSettings settings,
        Action<string>? log = null,
        Action<string, string>? onRestarting = null,
        bool enforceBrowserPolicy = true)
    {
        if (!HasAdminRights)
        {
            log?.Invoke(
                $"{UiCopy.MascotName} needs administrator rights to install the Firefox shield.");
            return false;
        }

        if (!IsConfigured || !Config.ExtensionGuardEnforceFirefox)
            return false;

        if (Config.ExtensionGuardFirefoxLocalMode)
            return false;

        if (enforceBrowserPolicy && !ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Firefox))
            return false;

        var cfg = ExtensionConfigResolver.Active;
        if (cfg is null || !cfg.IsFirefoxStoreReady)
            return false;

        if (!ApplyFirefoxSignedEnterprisePolicies(settings, requireDomToggle: enforceBrowserPolicy))
        {
            log?.Invoke(
                _imageShieldLastErrorOrDefault("Firefox policy could not be applied — see audit log."));
            return false;
        }

        var firefox = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);
        if (firefox is null)
            return false;

        var probe = new ExtensionPresenceProbe(cfg.ChromiumExtensionId, cfg.FirefoxAddonId);
        var presence = probe.Check(firefox);
        var profileInstall = FirefoxExtensionInstallState.ProbeProfileInstall(firefox, cfg.FirefoxAddonId);
        var heartbeat = ExtensionHeartbeatHub.Get(BrowserKind.Firefox);
        var heartbeatLive = new HttpExtensionLivenessProbe().IsLive(BrowserKind.Firefox);
        var policyReady = FirefoxExtensionInstallState.HasDistributionOrPolicy(cfg.FirefoxAddonId);
        var extensionMissing = !profileInstall.Present;
        var runtimeVersion = heartbeat?.Version ?? profileInstall.Version;
        var deployPending = FirefoxExtensionDeployStore.NeedsDeploy(cfg.Version);
        var extensionOutdated = deployPending
            || (runtimeVersion is not null
                && ExtensionPresenceProbe.CompareVersions(runtimeVersion, cfg.Version) < 0);

        if (!extensionOutdated
            && (profileInstall.Present || heartbeatLive == true)
            && RuntimeMatchesTarget(runtimeVersion, cfg.Version))
        {
            FirefoxExtensionDeployStore.MarkDeployed(cfg.Version);
            if (firefox.IsRunning())
            {
                log?.Invoke(
                    $"{UiCopy.MascotName} left Firefox open — the shield is already up to date.");
            }

            return false;
        }

        var needsCleanInstall = extensionMissing;
        var needsUpgrade = extensionOutdated;
        var shouldRestart = needsCleanInstall || needsUpgrade;

        AuditLog.Write(
            $"Firefox extension deploy check — cleanInstall={needsCleanInstall}, upgrade={needsUpgrade}, " +
            $"runtime={runtimeVersion ?? "none"}, profile={profileInstall.Present}, policyReady={policyReady}, target={cfg.Version}.");

        if (!shouldRestart)
        {
            if (profileInstall.Present && RuntimeMatchesTarget(runtimeVersion, cfg.Version))
                FirefoxExtensionDeployStore.MarkDeployed(cfg.Version);

            if (firefox.IsRunning())
            {
                log?.Invoke(
                    policyReady && extensionMissing
                        ? $"{UiCopy.MascotName} is waiting for Firefox to load the shield — restarting if needed."
                        : $"{UiCopy.MascotName} left Firefox open — the shield is already up to date.");
            }

            // Policy on disk but extension not in profile yet — still need a restart when Firefox is open.
            if (policyReady && extensionMissing && firefox.IsRunning()
                && BrowserRestartThrottle.ShouldRestart(BrowserKind.Firefox))
            {
                onRestarting?.Invoke(
                    ExtensionGuardCopy.StartupFirefoxStoreUpdateTitle,
                    ExtensionGuardCopy.StartupFirefoxStoreUpdateToast(cfg.Version));
                log?.Invoke($"{UiCopy.MascotName} is restarting Firefox to load the image shield.");
                BrowserRestartThrottle.MarkRestarted(BrowserKind.Firefox);
                return BrowserInstallOrchestrator.RestartBrowser(firefox, log);
            }

            return false;
        }

        if (needsCleanInstall)
        {
            FirefoxExtensionDeployStore.Clear();
            AuditLog.Write(
                $"Firefox clean install — enterprise policy v{cfg.Version} set; " +
                $"Firefox will install from {ResolveFirefoxInstallRef(cfg)} on next launch.");
        }

        if (needsUpgrade && extensionOutdated)
            PurgeFirefoxAddonProfiles(cfg.FirefoxAddonId, $"upgrade to v{cfg.Version}");

        if (!firefox.IsRunning())
        {
            log?.Invoke(
                needsCleanInstall
                    ? $"{UiCopy.MascotName} set up the Firefox shield — open Mozilla Firefox to install it."
                    : $"{UiCopy.MascotName} updated the Firefox shield — open Firefox to load version {cfg.Version}.");
            AuditLog.Write(
                needsCleanInstall
                    ? "Extension guard: Firefox not running — clean install policy ready."
                    : "Extension guard: Firefox not running — policy update complete; changes apply on next launch.");
            return true;
        }

        if (!BrowserRestartThrottle.ShouldRestart(BrowserKind.Firefox))
            return false;

        onRestarting?.Invoke(
            ExtensionGuardCopy.StartupFirefoxStoreUpdateTitle,
            ExtensionGuardCopy.StartupFirefoxStoreUpdateToast(cfg.Version));
        log?.Invoke(
            needsCleanInstall
                ? $"{UiCopy.MascotName} is restarting Mozilla Firefox for a clean shield install."
                : $"{UiCopy.MascotName} is restarting Firefox to load image shield {cfg.Version}.");
        AuditLog.Write(
            needsCleanInstall
                ? "Extension guard: restarting Firefox — clean install from signed XPI policy."
                : extensionOutdated
                ? $"Extension guard: restarting Firefox — extension {presence.Version} < {cfg.Version}."
                : $"Extension guard: restarting Firefox — deploy pending for v{cfg.Version}.");

        BrowserRestartThrottle.MarkRestarted(BrowserKind.Firefox);
        return BrowserInstallOrchestrator.RestartBrowser(firefox, log);
    }

    private static bool RuntimeMatchesTarget(string? runtimeVersion, string targetVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
            return true;

        if (string.IsNullOrWhiteSpace(runtimeVersion))
            return false;

        return ExtensionPresenceProbe.CompareVersions(runtimeVersion, targetVersion) >= 0;
    }

    private static string ResolveFirefoxInstallRef(ExtensionRuntimeConfig cfg)
    {
        foreach (var root in FirefoxInstallRoots.All())
        {
            var bundled = Path.Combine(root, "distribution", "extensions", cfg.FirefoxAddonId + ".xpi");
            if (File.Exists(bundled))
                return FirefoxExtensionPolicy.ToFileInstallUrl(bundled);
        }

        return cfg.FirefoxInstallUrl;
    }

    private static void PurgeFirefoxAddonProfiles(string addonId, string reason)
    {
        var (profilesPurged, purgeErrors) =
            FirefoxProfileExtensionInstaller.PurgeAddonFromAllProfiles(addonId);

        if (profilesPurged > 0)
        {
            AuditLog.Write(
                $"Firefox extension profile purge ({reason}) — {profilesPurged} profile(s).");
        }

        if (purgeErrors.Count > 0)
        {
            AuditLog.Write(
                $"Firefox extension profile purge warnings: {string.Join("; ", purgeErrors)}");
        }
    }

    private static bool IsFirefoxExtensionCurrent(ExtensionPresence presence, string targetVersion)
    {
        if (!presence.Present)
            return false;

        // Policy/distribution installs are often present but version is not readable from disk.
        if (presence.Version is null)
            return true;

        return ExtensionPresenceProbe.CompareVersions(presence.Version, targetVersion) >= 0;
    }

    private string _imageShieldLastErrorOrDefault(string fallback) =>
        LastError ?? ExtensionStoreConfigLoader.LastError ?? fallback;

    private bool EnsureFirefoxSignedPolicy(ExtensionRuntimeConfig cfg) =>
        ApplyFirefoxSignedEnterprisePolicies(ResolveFirefoxDeploySettings(), requireDomToggle: false);



    private bool ApplyPoliciesCore(ExtensionRuntimeConfig cfg, ImageShieldSettings tuning)

    {

        List<RegistryStringBackup> values = [];

        List<string> keysCreated = [];

        List<string> chromeErrors = [];

        List<string> chromiumProfileDirs = [];

        var chromiumRegistryBefore = 0;

        ChromiumExtensionPolicy.TryMigrateStaleDevPolicies(cfg.ChromiumExtensionId, cfg.ChromeUpdateUrl);



        if (Config.ExtensionGuardEnforceChromium)

        {

            chromiumRegistryBefore = values.Count;

            if (ChromiumUnpackedMode.IsActive)

            {

                var chromiumOutcome = ApplyChromiumUnpacked(cfg, tuning, ref values, ref keysCreated, ref chromeErrors);
                RecordChromiumDeployOutcome(chromiumOutcome);

            }

            else if (cfg.IsChromiumReady)

            {

                (values, keysCreated, chromeErrors) = _chromium.Apply(

                    cfg.ChromiumExtensionId,

                    cfg.ChromeUpdateUrl,

                    tuning);

                AuditLog.Write(ChromiumExtensionPolicy.DescribeForcelist(cfg.ChromiumExtensionId));

                RecordChromiumDeployOutcome(new ChromiumDeployOutcome(false, values.Count > chromiumRegistryBefore));

            }

        }



        List<string> firefoxApplied = [];

        List<string> firefoxBundled = [];

        List<string> firefoxProfileDirs = [];

        List<string> firefoxErrors = [];

        var firefoxMethod = ExtensionInstallMethod.NotApplicable;



        var firefox = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);

        if (firefox is not null
            && Config.ExtensionGuardEnforceFirefox
            && ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Firefox))

        {

            firefoxMethod = ExtensionInstallRouter.Resolve(firefox, cfg);

            if (firefoxMethod == ExtensionInstallMethod.FirefoxReleaseBlocked
                && Config.ExtensionGuardFirefoxLocalMode)
            {
                firefoxErrors.AddRange(_firefox.CleanupReleaseInstallRoots(cfg.FirefoxAddonId));
            }

            switch (firefoxMethod)

            {

                case ExtensionInstallMethod.FirefoxSignedEnterprise:

                {
                    var (signedApplied, signedBundled, signedErrors) = DeployFirefoxSignedPolicy(cfg, tuning);
                    firefoxApplied = signedApplied;
                    firefoxBundled = signedBundled;
                    firefoxErrors.AddRange(signedErrors);
                    break;
                }



                case ExtensionInstallMethod.FirefoxLocalUnsigned:

                    ApplyFirefoxLocalUnsigned(cfg, tuning, ref firefoxApplied, ref firefoxBundled, ref firefoxProfileDirs, firefoxErrors);

                    break;



                case ExtensionInstallMethod.FirefoxDeveloperEdition:

                    (firefoxApplied, firefoxErrors) = _firefox.ApplyPolicies(

                        cfg.FirefoxAddonId,

                        cfg.FirefoxInstallUrl,

                        xpiSourcePath: null,

                        allowUnsigned: true);

                    if (firefoxApplied.Count > 0)
                        firefoxErrors.AddRange(_firefox.UpdateRuntimeSettings(cfg.FirefoxAddonId, tuning).Errors);

                    break;

            }

        }



        var errors = chromeErrors.Concat(firefoxErrors).ToList();



        if (values.Count == 0 && firefoxApplied.Count == 0 && chromiumProfileDirs.Count == 0)

        {

            if (errors.Count > 0)

            {

                LastError = string.Join("; ", errors);

                AuditLog.Write($"Image shield policy warnings: {LastError}");

            }



            IsActive = errors.Count == 0;

            AuditLog.Write(

                $"Image shield ready — Chromium values: 0, Firefox policies: 0, " +

                $"Firefox mode: {ExtensionInstallRouter.Describe(firefoxMethod)}.");

            return IsActive;

        }



        _backupStore.Save(new ImageShieldState

        {

            RegistryValues = values,

            RegistryKeysCreated = keysCreated,

            FirefoxAddonIds = firefoxApplied.Count > 0 ? [cfg.FirefoxAddonId] : [],

            FirefoxBundledXpiPaths = firefoxBundled,

            FirefoxProfileUnpackedDirs = firefoxProfileDirs,

            ChromiumProfileUnpackedDirs = chromiumProfileDirs,

        });



        RuntimeFilteringActive = tuning.ShieldActive;
        ImageShieldRuntimeStore.SetFilteringActive(tuning.ShieldActive);
        IsActive = true;

        LastError = errors.Count > 0 ? string.Join("; ", errors) : null;

        NativeMessagingHostRegistry.Register();

        AuditLog.Write(

            $"Image shield policies ready — Chromium values: {values.Count}, Firefox policies: {firefoxApplied.Count}, " +

            $"Firefox mode: {ExtensionInstallRouter.Describe(firefoxMethod)}.");

        if (LastError is not null)

            AuditLog.Write($"Image shield partial warnings: {LastError}");



        return true;

    }



    private ChromiumDeployOutcome ApplyChromiumUnpacked(
        ExtensionRuntimeConfig cfg,
        ImageShieldSettings tuning,
        ref List<RegistryStringBackup> values,
        ref List<string> keysCreated,
        ref List<string> errors)
    {
        var (ok, filesChanged, deployErrors) = ChromiumUnpackedDeployer.EnsureDeployed();
        errors.AddRange(deployErrors);
        if (!ok)
            return new ChromiumDeployOutcome(false, false);

        var stripped = ChromiumExtensionPolicy.StripForcelist(cfg.ChromiumExtensionId);

        var (registryValues, registryKeys, applyErrors) = _chromium.Apply(
            cfg.ChromiumExtensionId,
            updateUrl: null,
            tuning);
        values.AddRange(registryValues);
        keysCreated.AddRange(registryKeys);
        errors.AddRange(applyErrors);

        if (filesChanged || registryValues.Count > 0 || stripped)
        {
            AuditLog.Write(
                $"Chromium shield deployed (unpacked) — path: {ChromiumUnpackedDeployer.DeployRoot}, " +
                $"managed policy keys: {registryValues.Count}.");
        }

        return new ChromiumDeployOutcome(filesChanged, registryValues.Count > 0 || stripped);
    }



    private bool EnsureChromiumUnpackedPolicy(ExtensionRuntimeConfig cfg)

    {

        List<RegistryStringBackup> values = [];

        List<string> keysCreated = [];

        var errors = new List<string>();



        var outcome = ApplyChromiumUnpacked(cfg, ImageShieldSettings.Default, ref values, ref keysCreated, ref errors);
        RecordChromiumDeployOutcome(outcome);

        if (values.Count == 0 && !outcome.ExtensionFilesChanged)

            return false;



        var existing = _backupStore.Load() ?? new ImageShieldState();

        _backupStore.Save(new ImageShieldState

        {

            RegistryValues = values.Count > 0 ? values : existing.RegistryValues,

            RegistryKeysCreated = keysCreated.Count > 0 ? keysCreated : existing.RegistryKeysCreated,

            FirefoxAddonIds = existing.FirefoxAddonIds,

            FirefoxBundledXpiPaths = existing.FirefoxBundledXpiPaths,

            FirefoxProfileUnpackedDirs = existing.FirefoxProfileUnpackedDirs,

            ChromiumProfileUnpackedDirs = existing.ChromiumProfileUnpackedDirs,

        });



        if (errors.Count > 0)

            AuditLog.Write($"Chromium unpacked bundle warnings: {string.Join("; ", errors)}");



        return true;

    }



    private FirefoxDeployOutcome ApplyFirefoxLocalUnsigned(

        ExtensionRuntimeConfig cfg,

        ImageShieldSettings tuning,

        ref List<string> firefoxApplied,

        ref List<string> firefoxBundled,

        ref List<string> firefoxProfileDirs,

        List<string> firefoxErrors)

    {

        var distDir = FirefoxLocalPackager.FindFirefoxDistDir();
        if (distDir is null)
        {
            firefoxErrors.Add("Firefox extension dist folder missing — run npm run build:firefox.");
            return new FirefoxDeployOutcome(false, false);
        }

        List<string> policyPaths;
        bool extensionFilesChanged;
        bool policiesChanged;
        (policyPaths, firefoxBundled, extensionFilesChanged, policiesChanged, var localErrors) =
            _firefox.ApplyLocalUnsigned(cfg.FirefoxAddonId, distDir, tuning);
        firefoxApplied = policyPaths;
        firefoxErrors.AddRange(localErrors);

        (firefoxProfileDirs, var profileFilesChanged, var unpackErrors) =
            FirefoxProfileExtensionInstaller.DeployUnpacked(cfg.FirefoxAddonId, distDir);
        firefoxErrors.AddRange(unpackErrors);
        extensionFilesChanged |= profileFilesChanged;

        var distUnpacked = firefoxBundled
            .FirstOrDefault(p => Directory.Exists(p)
                && File.Exists(Path.Combine(p, "manifest.json")));

        firefoxBundled = firefoxBundled
            .Concat(firefoxProfileDirs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (firefoxApplied.Count > 0)
        {
            AuditLog.Write(
                $"Firefox shield deployed (unpacked) — policies: {firefoxApplied.Count}, " +
                $"profile copies: {firefoxProfileDirs.Count}, distro: {distUnpacked ?? "missing"}.");
            AuditLog.Write($"Firefox profiles: {FirefoxProfileDiscovery.DescribeProfilesForAudit()}");
            if (firefoxErrors.Count > 0)
                AuditLog.Write($"Firefox shield deploy warnings: {string.Join("; ", firefoxErrors)}");
        }

        var outcome = new FirefoxDeployOutcome(extensionFilesChanged, policiesChanged);
        RecordFirefoxDeployOutcome(outcome);
        return outcome;
    }



    private bool EnsureFirefoxLocalPolicy(ExtensionRuntimeConfig cfg)

    {

        List<string> applied = [];

        List<string> bundled = [];

        List<string> profileDirs = [];

        var errors = new List<string>();



        ApplyFirefoxLocalUnsigned(cfg, ImageShieldSettings.Default, ref applied, ref bundled, ref profileDirs, errors);

        if (applied.Count == 0)

            return false;



        var existing = _backupStore.Load() ?? new ImageShieldState();

        _backupStore.Save(new ImageShieldState

        {

            RegistryValues = existing.RegistryValues,

            RegistryKeysCreated = existing.RegistryKeysCreated,

            FirefoxAddonIds = [cfg.FirefoxAddonId],

            FirefoxBundledXpiPaths = bundled,

            FirefoxProfileUnpackedDirs = profileDirs,

        });



        if (errors.Count > 0)
            AuditLog.Write($"Firefox local bundle warnings: {string.Join("; ", errors)}");

        return true;
    }

    /// <summary>
    /// On Guardi startup, syncs the local Firefox bundle and restarts Firefox Dev Edition
    /// only when extension files or policies actually changed.
    /// </summary>
    public bool TrySyncFirefoxDevOnStartup(
        ImageShieldSettings settings,
        Action<string>? log = null,
        Action<string, string>? onRestarting = null)
    {
        if (_firefoxStartupSyncDone)
            return false;

        _firefoxStartupSyncDone = true;

        if (!HasAdminRights || !IsConfigured || !Config.ExtensionGuardFirefoxLocalMode)
            return false;

        BrowserRestartThrottle.Reset(BrowserKind.Firefox);

        var outcome = _lastFirefoxDeployOutcome ?? EvaluateFirefoxDeploy(settings);
        if (outcome is null || !outcome.RequiresBrowserRestart)
        {
            var firefox = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);
            if (firefox?.IsRunning() == true)
            {
                log?.Invoke(
                    $"{UiCopy.MascotName} left Firefox Developer Edition open — the shield is already up to date.");
            }

            return false;
        }

        var browser = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);
        if (browser is null)
            return false;

        if (!browser.IsRunning())
        {
            AuditLog.Write(
                "Extension guard: Firefox Dev Edition not running — deploy complete; " +
                "changes apply on next browser launch.");
            return false;
        }

        onRestarting?.Invoke(ExtensionGuardCopy.StartupFirefoxRestartTitle, ExtensionGuardCopy.StartupFirefoxRestartToast);
        log?.Invoke(
            $"{UiCopy.MascotName} is restarting Firefox Developer Edition so supervision can start.");
        AuditLog.Write("Extension guard: restarting open Firefox Dev Edition to load supervision.");

        BrowserRestartThrottle.MarkRestarted(BrowserKind.Firefox);
        return BrowserInstallOrchestrator.RestartBrowser(browser, log);
    }

    /// <summary>
    /// Applies Chromium policies and restarts Chrome when the shield is missing or policies changed.
    /// </summary>
    public bool TryRestartChromeForShield(
        ImageShieldSettings settings,
        Action<string>? log = null,
        Action<string, string>? onRestarting = null)
    {
        if (!HasAdminRights || !IsConfigured || !Config.ExtensionGuardEnforceChromium)
            return false;

        // Extension not published yet (and not in local unpacked dev) — nothing to install,
        // Chromium browsers are being blocked instead. Don't restart Chrome for the shield.
        if (!Config.ChromiumExtensionPublished && !ChromiumUnpackedMode.IsActive)
            return false;

        if (!ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Chrome))
            return false;

        var cfg = ExtensionConfigResolver.Active;
        if (cfg is null)
            return false;

        if (ChromiumUnpackedMode.IsActive && !ChromiumUnpackedDeployer.HasSource())
        {
            log?.Invoke("Chromium extension not built — run: cd extension && npm.cmd run build:chromium");
            return false;
        }

        if (!ChromiumUnpackedMode.IsActive && !cfg.IsChromiumReady)
            return false;

        ApplyPoliciesOnly(settings);

        var chrome = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Chrome);
        if (chrome is null)
            return false;

        var extensionMissing = !ChromiumExtensionInstallState.IsInstalled(chrome, cfg.ChromiumExtensionId);

        // Web Store mode: if the extension isn't published/listed on the Chrome Web Store yet,
        // restarting Chrome cannot install it — the browser would just be bounced forever in an
        // incoherent loop. Leave Chrome open; the install resumes automatically after publication
        // (mirrors ExtensionEnforcementService's NotListed guard). Unpacked mode still restarts,
        // since --load-extension does load the local build on relaunch.
        if (extensionMissing
            && !ChromiumUnpackedMode.IsActive
            && ChromiumWebStoreProbe.Check(cfg.ChromiumExtensionId).Status == ChromiumWebStoreListingStatus.NotListed)
        {
            return false;
        }

        var shouldRestart = extensionMissing
            || (_lastChromiumDeployOutcome?.RequiresBrowserRestart ?? false);

        if (!shouldRestart)
        {
            if (chrome.IsRunning())
            {
                log?.Invoke(
                    $"{UiCopy.MascotName} left Chrome open — the shield is already up to date.");
            }

            return false;
        }

        if (!chrome.IsRunning())
        {
            AuditLog.Write(
                "Extension guard: Chrome not running — deploy complete; changes apply on next launch.");
            return false;
        }

        if (!BrowserRestartThrottle.ShouldRestart(BrowserKind.Chrome))
            return false;

        onRestarting?.Invoke(
            ExtensionGuardCopy.StartupChromiumRestartTitle,
            ExtensionGuardCopy.StartupChromiumRestartToast);
        log?.Invoke($"{UiCopy.MascotName} is restarting Chrome so the image shield can load.");
        AuditLog.Write(
            extensionMissing
                ? "Extension guard: restarting Chrome — extension not detected in profile."
                : "Extension guard: restarting Chrome to load updated shield bundle.");

        BrowserRestartThrottle.MarkRestarted(BrowserKind.Chrome);
        return BrowserInstallOrchestrator.RestartBrowser(chrome, log);
    }

    public bool TrySyncChromiumOnStartup(
        ImageShieldSettings settings,
        Action<string>? log = null,
        Action<string, string>? onRestarting = null)
    {
        if (_chromiumStartupSyncDone)
            return false;

        _chromiumStartupSyncDone = true;
        BrowserRestartThrottle.Reset(BrowserKind.Chrome);
        return TryRestartChromeForShield(settings, log, onRestarting);
    }

    private void RecordChromiumDeployOutcome(ChromiumDeployOutcome outcome)
    {
        if (_lastChromiumDeployOutcome is null)
        {
            _lastChromiumDeployOutcome = outcome;
            return;
        }

        _lastChromiumDeployOutcome = new ChromiumDeployOutcome(
            _lastChromiumDeployOutcome.ExtensionFilesChanged || outcome.ExtensionFilesChanged,
            _lastChromiumDeployOutcome.PoliciesChanged || outcome.PoliciesChanged);
    }

    public FirefoxDeployOutcome? EvaluateFirefoxDeploy(ImageShieldSettings settings)
    {
        if (!HasAdminRights || !IsConfigured || !Config.ExtensionGuardFirefoxLocalMode)
            return null;

        var cfg = ExtensionConfigResolver.Active;
        if (cfg is null)
            return null;

        var firefox = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == BrowserKind.Firefox);
        if (firefox is null
            || ExtensionInstallRouter.Resolve(firefox, cfg) != ExtensionInstallMethod.FirefoxLocalUnsigned)
        {
            return null;
        }

        List<string> applied = [];
        List<string> bundled = [];
        List<string> profileDirs = [];
        var errors = new List<string>();
        return ApplyFirefoxLocalUnsigned(cfg, settings, ref applied, ref bundled, ref profileDirs, errors);
    }

    private void RecordFirefoxDeployOutcome(FirefoxDeployOutcome outcome)
    {
        if (_lastFirefoxDeployOutcome is null)
        {
            _lastFirefoxDeployOutcome = outcome;
            return;
        }

        _lastFirefoxDeployOutcome = new FirefoxDeployOutcome(
            _lastFirefoxDeployOutcome.ExtensionFilesChanged || outcome.ExtensionFilesChanged,
            _lastFirefoxDeployOutcome.PoliciesChanged || outcome.PoliciesChanged);
    }

    private bool EnsureFirefoxDevPolicy(ExtensionRuntimeConfig cfg)

    {

        var source = ExtensionBundleLocator.FindExtensionSourceRoot();

        if (source is null)

            return false;



        var outputDir = Path.Combine(source, "web-ext-output");

        var xpiPath = Directory.Exists(outputDir)

            ? Directory.EnumerateFiles(outputDir, "*.xpi").FirstOrDefault()

            : null;



        if (xpiPath is null)

            return false;



        var (bundled, errors) = _firefox.DeployBundledOnly(cfg.FirefoxAddonId, xpiPath);

        if (bundled.Count == 0)

            return false;



        var existing = _backupStore.Load() ?? new ImageShieldState();

        _backupStore.Save(new ImageShieldState

        {

            RegistryValues = existing.RegistryValues,

            RegistryKeysCreated = existing.RegistryKeysCreated,

            FirefoxAddonIds = [cfg.FirefoxAddonId],

            FirefoxBundledXpiPaths = bundled,

            FirefoxProfileUnpackedDirs = [],

        });



        if (errors.Count > 0)

            AuditLog.Write($"Firefox dev bundle warnings: {string.Join("; ", errors)}");



        return true;

    }



    private FirefoxDeployOutcome RefreshFirefoxExtensionInstall(ExtensionRuntimeConfig cfg, ImageShieldSettings tuning)
    {
        if (!Config.ExtensionGuardFirefoxLocalMode || !FirefoxEditionHelper.CanInstallLocalUnsigned)
            return new FirefoxDeployOutcome(false, false);

        List<string> applied = [];
        List<string> bundled = [];
        List<string> profileDirs = [];
        var errors = new List<string>();
        var outcome = ApplyFirefoxLocalUnsigned(cfg, tuning, ref applied, ref bundled, ref profileDirs, errors);
        if (errors.Count > 0)
            AuditLog.Write($"Firefox extension refresh warnings: {string.Join("; ", errors)}");
        return outcome;
    }

    private void UpdateRuntimePoliciesOnly(ExtensionRuntimeConfig cfg, ImageShieldSettings settings)

    {

        var errors = new List<string>();



        if (Config.ExtensionGuardEnforceChromium && cfg.IsChromiumReady
            && (HasPersistedInstall || ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Chrome)
                || ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Edge)
                || ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Brave)))

            errors.AddRange(_chromium.UpdateManagedSettings(cfg.ChromiumExtensionId, settings));



        if (Config.ExtensionGuardEnforceFirefox
            && (HasPersistedInstall || ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Firefox)))

        {

            var (runtimeErrors, policiesChanged) = _firefox.UpdateRuntimeSettings(cfg.FirefoxAddonId, settings);
            errors.AddRange(runtimeErrors);
            if (policiesChanged)
                RecordFirefoxDeployOutcome(new FirefoxDeployOutcome(false, true));

        }



        if (errors.Count > 0)

            AuditLog.Write($"Image shield runtime policy warnings: {string.Join("; ", errors)}");

    }



    private void ReleaseOrphanedState()

    {

        // Install persists across Guardi sessions — backup state is expected.

    }

}

