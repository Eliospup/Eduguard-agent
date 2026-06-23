using System.Diagnostics;
using System.Runtime.Versioning;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Blocks browsers missing the shield. Store policies install automatically;
/// Guardi waits, restarts once, then closes the browser if still unprotected.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExtensionEnforcementService : IDisposable
{
    private readonly IExtensionLivenessProbe _liveness;
    private readonly Action<string>? _log;
    private readonly Action<ProtectedBrowser>? _ensureBrowser;
    private readonly Action? _prepareFirefoxUpgrade;
    private readonly TimeSpan _installGrace;
    private readonly TimeSpan _restartCooldown;

    private readonly object _lock = new();
    private readonly Dictionary<BrowserKind, BrowserInstallTrack> _installTracks = new();
    private readonly HashSet<int> _reportedUnsupportedPids = new();

    private Timer? _timer;
    private bool _running;
    private bool _disposed;
    private volatile bool _overlaySuppressed;
    private string? _lastStateSignature;
    private bool _methodsLogged;
    private bool _storeNotListedLogged;

    public ExtensionEnforcementService(
        Action<string>? log = null,
        Action<ProtectedBrowser>? ensureBrowser = null,
        IExtensionLivenessProbe? liveness = null,
        Action? prepareFirefoxUpgrade = null)
    {
        _log = log;
        _ensureBrowser = ensureBrowser;
        _prepareFirefoxUpgrade = prepareFirefoxUpgrade;
        _liveness = liveness ?? new HttpExtensionLivenessProbe();
        _installGrace = TimeSpan.FromSeconds(Math.Max(120, Config.ExtensionInstallGraceSeconds));
        _restartCooldown = TimeSpan.FromSeconds(90);
    }

    public event Action<ExtensionGuardState?>? StateChanged;
    public event Action<string>? UnsupportedBrowserBlocked;

    public bool IsConfigured => ExtensionConfigResolver.IsReady;

    public void DismissOverlay()
    {
        _overlaySuppressed = true;
        lock (_lock)
            _lastStateSignature = null;
        StateChanged?.Invoke(null);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _running || !Config.ExtensionGuardEnabled)
                return;

            _running = true;
            _timer = new Timer(_ => SafeEvaluate(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(Config.ExtensionGuardTickMs));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
            _installTracks.Clear();
            _lastStateSignature = null;
            _methodsLogged = false;
            _storeNotListedLogged = false;
        }

        StateChanged?.Invoke(null);
    }

    public void ForceCheck() => SafeEvaluate();

    private void SafeEvaluate()
    {
        try
        {
            Evaluate();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Extension guard error: {ex.Message}");
        }
    }

    private void Evaluate()
    {
        lock (_lock)
        {
            if (_disposed || !_running)
                return;
        }

        if (Config.BlockUnsupportedBrowsers)
            BlockUnsupportedBrowsers();

        if (!Config.ExtensionGuardEnabled || !IsConfigured)
        {
            EmitState(null);
            return;
        }

        var cfg = ExtensionConfigResolver.Active!;
        LogInstallMethodsOnce(cfg);

        var presenceProbe = new ExtensionPresenceProbe(cfg.ChromiumExtensionId, cfg.FirefoxAddonId);
        var now = DateTimeOffset.UtcNow;
        var restarting = new List<string>();
        var installing = new List<string>();
        var storePending = new List<string>();
        var actionRequired = new List<string>();
        var blocked = new List<string>();
        var outdated = new List<string>();

        if (FirefoxEditionHelper.ShouldBlockRelease
            && ImageShieldPolicy.ShouldEnforceBrowser(BrowserKind.Firefox))
            BlockFirefoxRelease();

        foreach (var browser in BrowserCatalog.Protected)
        {
            var method = ExtensionInstallRouter.Resolve(browser, cfg);
            if (method == ExtensionInstallMethod.NotApplicable)
            {
                ClearInstallTrack(browser.Kind);
                continue;
            }

            if (!BrowserIsActive(browser))
                continue;

            var presence = presenceProbe.Check(browser);
            var live = _liveness.IsLive(browser.Kind);
            var policyReady = browser.Kind == BrowserKind.Firefox
                && method == ExtensionInstallMethod.FirefoxSignedEnterprise
                && FirefoxExtensionInstallState.HasDistributionOrPolicy(cfg.FirefoxAddonId);
            // Profile install or live heartbeat — policy/distribution on disk alone is not "active".
            var active = presence.Present || live == true;

            if (active)
            {
                if (live == false)
                    HandleUnresponsive(browser, restarting, now);
                else if (IsOutdated(presence, browser.Kind))
                    HandleOutdated(browser, presence, outdated, now);
                else
                    ClearInstallTrack(browser.Kind);
                continue;
            }

            if (method == ExtensionInstallMethod.FirefoxReleaseBlocked)
            {
                blocked.Add(browser.EffectiveDisplayName);
                BrowserInstallOrchestrator.CloseBrowser(browser);
                continue;
            }

            var track = GetOrCreateTrack(browser.Kind);
            if (!track.InstallEngaged)
            {
                track.InstallEngaged = true;
                track.FirstDetectedAt = now;
                _overlaySuppressed = false;
                _log?.Invoke($"{browser.EffectiveDisplayName} opened without the shield — {ExtensionInstallRouter.Describe(method)}");
            }

            if (live == false && !track.LiveFailureLogged)
            {
                track.LiveFailureLogged = true;
                AuditLog.Write(
                    $"{browser.EffectiveDisplayName}: shield present on disk but extension background is not responding.");
            }

            HandleMissingExtension(browser, method, cfg, policyReady, track, restarting, installing, storePending, actionRequired, now);
        }

        ExtensionGuardState? state =
            blocked.Count > 0 ? ExtensionGuardCopy.Unsupported(Distinct(blocked))
            : storePending.Count > 0 ? ExtensionGuardCopy.StorePending(Distinct(storePending))
            : actionRequired.Count > 0 ? ExtensionGuardCopy.ActionRequired(Distinct(actionRequired))
            : restarting.Count > 0 ? ExtensionGuardCopy.Restarting(Distinct(restarting))
            : installing.Count > 0 ? ExtensionGuardCopy.Installing(Distinct(installing))
            : outdated.Count > 0 ? ExtensionGuardCopy.Outdated(Distinct(outdated))
            : null;

        if (_overlaySuppressed)
            EmitState(null);
        else
            EmitState(state);
    }

    private static bool BrowserIsActive(ProtectedBrowser browser) =>
        browser.HasInteractiveWindow()
        || (browser.Engine == BrowserEngine.Chromium && browser.IsRunning())
        || (browser.Kind == BrowserKind.Firefox && browser.IsRunning());

    private void LogInstallMethodsOnce(ExtensionRuntimeConfig cfg)
    {
        if (_methodsLogged)
            return;

        _methodsLogged = true;
        foreach (var browser in BrowserCatalog.Protected)
        {
            if (!browser.IsInstalled())
                continue;
            var method = ExtensionInstallRouter.Resolve(browser, cfg);
            AuditLog.Write($"Extension install method — {browser.EffectiveDisplayName}: {ExtensionInstallRouter.Describe(method)}");
        }
    }

    private void HandleMissingExtension(
        ProtectedBrowser browser,
        ExtensionInstallMethod method,
        ExtensionRuntimeConfig cfg,
        bool policyReady,
        BrowserInstallTrack track,
        List<string> restarting,
        List<string> installing,
        List<string> storePending,
        List<string> actionRequired,
        DateTimeOffset now)
    {
        if (!track.PayloadDeployed)
        {
            try
            {
                _ensureBrowser?.Invoke(browser);
                track.PayloadDeployed = true;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Shield policy apply for {browser.EffectiveDisplayName} failed: {ex.Message}");
            }
        }

        if (method == ExtensionInstallMethod.ChromiumWebStore
            && browser.Engine == BrowserEngine.Chromium)
        {
            var probe = ChromiumWebStoreProbe.Check(cfg.ChromiumExtensionId);
            if (probe.Status == ChromiumWebStoreListingStatus.NotListed)
            {
                storePending.Add(browser.EffectiveDisplayName);
                if (!_storeNotListedLogged)
                {
                    _storeNotListedLogged = true;
                    AuditLog.Write(
                        $"Chrome Web Store preflight: extension {cfg.ChromiumExtensionId} not listed yet " +
                        $"({probe.Detail ?? "noupdate"}). Chrome stays open; install will resume after publication.");
                    _log?.Invoke(
                        $"{browser.EffectiveDisplayName}: shield policy is set, but the extension isn't on the Chrome Web Store yet.");
                }

                return;
            }

            if (probe.Status == ChromiumWebStoreListingStatus.Listed)
                _storeNotListedLogged = false;
        }

        var firstSeen = track.FirstDetectedAt ?? now;
        var elapsed = now - firstSeen;
        var restartDelay = method switch
        {
            ExtensionInstallMethod.ChromiumUnpackedSideload => TimeSpan.FromSeconds(3),
            ExtensionInstallMethod.FirefoxSignedEnterprise => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(15),
        };

        if (elapsed < _installGrace)
        {
            installing.Add(browser.EffectiveDisplayName);

            var allowRestart = track.LastRestartAt is null || now - track.LastRestartAt > _restartCooldown;

            if (allowRestart
                && elapsed > restartDelay
                && BrowserRestartThrottle.ShouldRestart(browser.Kind))
            {
                track.LastRestartAt = now;
                restarting.Add(browser.EffectiveDisplayName);
                BrowserRestartThrottle.MarkRestarted(browser.Kind);
                BrowserInstallOrchestrator.RestartBrowser(browser, _log);
            }

            return;
        }

        actionRequired.Add(browser.EffectiveDisplayName);

        // Enterprise policy is set but the profile never picked up the XPI — keep restarting
        // instead of closing Firefox (Release ignores unsigned sideload kills).
        if (browser.Kind == BrowserKind.Firefox
            && method == ExtensionInstallMethod.FirefoxSignedEnterprise
            && policyReady)
        {
            if (BrowserRestartThrottle.ShouldRestart(browser.Kind)
                && (track.LastRestartAt is null || now - track.LastRestartAt > _restartCooldown))
            {
                track.LastRestartAt = now;
                restarting.Add(browser.EffectiveDisplayName);
                BrowserRestartThrottle.MarkRestarted(browser.Kind);
                BrowserInstallOrchestrator.RestartBrowser(browser, _log);
                if (!track.FailureLogged)
                {
                    track.FailureLogged = true;
                    _log?.Invoke(
                        $"{browser.EffectiveDisplayName} shield policy is set but the extension is not loaded — restarting Firefox.");
                    AuditLog.Write(
                        "Extension guard: Firefox enterprise policy deployed — restarting to load shield into profile.");
                }
            }
            else if (!track.FailureLogged)
            {
                track.FailureLogged = true;
                _log?.Invoke(
                    $"{browser.EffectiveDisplayName} shield is still starting — check about:addons and about:policies.");
                AuditLog.Write(
                    "Extension guard: Firefox policy set but extension not in profile yet.");
            }

            return;
        }

        BrowserInstallOrchestrator.CloseBrowser(browser);

        if (!track.FailureLogged)
        {
            track.FailureLogged = true;
            _log?.Invoke(
                $"{browser.EffectiveDisplayName} still has no shield after {(int)_installGrace.TotalSeconds}s — " +
                (method == ExtensionInstallMethod.ChromiumUnpackedSideload && Config.ExtensionGuardEnforceChromium
                    ? "restart Chrome with Guardi running — extension loads via --load-extension."
                    : method == ExtensionInstallMethod.ChromiumWebStore
                    ? "check chrome://policy (CWS forcelist) and chrome://extensions."
                    : Config.ExtensionGuardFirefoxLocalMode && !Config.ExtensionGuardEnforceChromium
                    ? "restart Firefox and check about:policies / about:addons."
                    : "check store-config.json and that the extension is published on the store."));
        }
    }

    private void HandleUnresponsive(
        ProtectedBrowser browser,
        List<string> restarting,
        DateTimeOffset now)
    {
        var track = GetOrCreateTrack(browser.Kind);
        if (track.LastRestartAt is { } last && now - last < _restartCooldown)
            return;

        if (!BrowserRestartThrottle.ShouldRestart(browser.Kind))
            return;

        track.LastRestartAt = now;
        restarting.Add(browser.EffectiveDisplayName);
        BrowserRestartThrottle.MarkRestarted(browser.Kind);
        AuditLog.Write(
            $"{browser.EffectiveDisplayName}: extension installed but not responding — restarting browser.");
        _log?.Invoke(
            $"{browser.EffectiveDisplayName} shield is installed but not responding — restarting to wake it up.");
        BrowserInstallOrchestrator.RestartBrowser(browser, _log);
    }

    private void HandleOutdated(
        ProtectedBrowser browser,
        ExtensionPresence presence,
        List<string> outdated,
        DateTimeOffset now)
    {
        outdated.Add(browser.EffectiveDisplayName);
        var track = GetOrCreateTrack(browser.Kind);
        if (track.LastRestartAt is { } last && now - last < _restartCooldown)
            return;

        if (browser.Kind == BrowserKind.Firefox
            && FirefoxEditionHelper.UseSignedReleaseTarget
            && IsOutdated(presence, browser.Kind))
        {
            _prepareFirefoxUpgrade?.Invoke();
            AuditLog.Write(
                $"Extension guard: outdated Firefox shield {presence.Version} < target — redeploy + restart.");
        }

        track.LastRestartAt = now;
        if (BrowserRestartThrottle.ShouldRestart(browser.Kind))
        {
            BrowserRestartThrottle.MarkRestarted(browser.Kind);
            BrowserInstallOrchestrator.RestartBrowser(browser, _log);
        }
    }

    private bool IsOutdated(ExtensionPresence presence, BrowserKind kind)
    {
        var target = !string.IsNullOrWhiteSpace(Config.ImageShieldMinVersion)
            ? Config.ImageShieldMinVersion
            : ExtensionConfigResolver.Active?.Version;

        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (kind == BrowserKind.Firefox && FirefoxExtensionDeployStore.NeedsDeploy(target))
            return true;

        var version = presence.Version;
        var beat = ExtensionHeartbeatHub.Get(kind);
        if (!string.IsNullOrWhiteSpace(beat?.Version))
            version = beat.Version;

        if (version is null)
            return false;

        return ExtensionPresenceProbe.CompareVersions(version, target) < 0;
    }

    private BrowserInstallTrack GetOrCreateTrack(BrowserKind kind)
    {
        lock (_lock)
        {
            if (!_installTracks.TryGetValue(kind, out var track))
            {
                track = new BrowserInstallTrack();
                _installTracks[kind] = track;
            }

            return track;
        }
    }

    private void ClearInstallTrack(BrowserKind kind)
    {
        lock (_lock)
            _installTracks.Remove(kind);
    }

    private void BlockFirefoxRelease()
    {
        PruneReportedPids();

        var reportNew = false;
        foreach (var process in FirefoxEditionHelper.GetFirefoxProcesses())
        {
            try
            {
                if (!FirefoxEditionHelper.IsReleaseProcess(process))
                    continue;

                var pid = process.Id;
                lock (_lock)
                {
                    if (_reportedUnsupportedPids.Add(pid))
                        reportNew = true;
                }

                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (reportNew)
        {
            AuditLog.Write("Extension guard: closed Mozilla Firefox Release (use Developer Edition).");
            UnsupportedBrowserBlocked?.Invoke("Mozilla Firefox");
        }
    }

    private void BlockUnsupportedBrowsers()
    {
        PruneReportedPids();

        foreach (var (processName, displayName) in BrowserCatalog.Unsupported)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            var reportNew = false;
            foreach (var process in processes)
            {
                try
                {
                    var pid = process.Id;
                    if (pid == Environment.ProcessId)
                        continue;

                    lock (_lock)
                    {
                        if (_reportedUnsupportedPids.Add(pid))
                            reportNew = true;
                    }

                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best effort
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (reportNew)
            {
                _log?.Invoke($"Closed {displayName} — Guardi cannot protect that browser.");
                UnsupportedBrowserBlocked?.Invoke(displayName);
            }
        }
    }

    private void PruneReportedPids()
    {
        int[] snapshot;
        lock (_lock)
            snapshot = _reportedUnsupportedPids.ToArray();

        foreach (var pid in snapshot)
        {
            if (IsProcessAlive(pid))
                continue;
            lock (_lock)
                _reportedUnsupportedPids.Remove(pid);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void EmitState(ExtensionGuardState? state)
    {
        var signature = state is null
            ? null
            : $"{state.Phase}|{string.Join(",", state.Browsers)}";

        lock (_lock)
        {
            if (signature == _lastStateSignature)
                return;
            _lastStateSignature = signature;
        }

        StateChanged?.Invoke(state);
    }

    private static List<string> Distinct(List<string> names) =>
        names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _running = false;
            _timer?.Dispose();
            _timer = null;
            _installTracks.Clear();
            _reportedUnsupportedPids.Clear();
        }
    }

    private sealed class BrowserInstallTrack
    {
        public DateTimeOffset? FirstDetectedAt;
        public DateTimeOffset? LastRestartAt;
        public bool InstallEngaged;
        public bool PayloadDeployed;
        public bool FailureLogged;
        public bool LiveFailureLogged;
    }
}
