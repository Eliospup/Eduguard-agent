using System.Runtime.Versioning;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Autonomous, browser-independent watchdog that keeps the extension force-install policy
/// intact across both engines:
///  - Firefox: distribution/policies.json + the bundled XPI it points at (under Program Files);
///  - Chromium: the ExtensionInstallForcelist registry key (under HKLM\SOFTWARE\Policies\...).
///
/// Both live in admin-only locations, so a standard supervised user can't touch them — but one
/// with local admin can delete them to unload the shield. Before this guard, nothing rewrote
/// them until the browser was relaunched and the extension was already detected missing,
/// leaving an unprotected browsing window (the hosts and DNS layers still block known domains,
/// but the on-device image blur and page-text scoring go with the extension).
///
/// This closes it the same way the hosts (UrlBlockingService) and DNS
/// (FamilyDnsShieldService) layers are re-asserted. A FileSystemWatcher restores the Firefox
/// policy file within milliseconds; the registry has no cheap file watcher, so the Chromium
/// forcelist is covered by the periodic timer (which is also the backstop for missed Firefox
/// events). The intact checks are a few file/registry reads with no writes; repair only runs
/// when tampering is actually detected, and is debounced. The Chromium repair is deliberately
/// backup-free so it never corrupts the uninstall/teardown backup.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExtensionPolicyWatchdog : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MinRepairGap = TimeSpan.FromSeconds(3);

    private readonly FirefoxExtensionPolicy _firefox = new();
    private readonly ChromiumExtensionPolicy _chromium = new();
    private readonly Func<bool> _shouldEnforce;
    private readonly Func<string?> _firefoxAddonId;
    private readonly Action _repairFirefox;
    private readonly Func<(string ExtensionId, string UpdateUrl)?> _chromiumTarget;
    private readonly Action<string>? _log;

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _timer;
    private bool _running;
    private bool _disposed;
    private DateTimeOffset _lastRepairUtc = DateTimeOffset.MinValue;

    /// <param name="shouldEnforce">
    /// True only while the shield is meant to be on — keeps the watchdog from fighting a
    /// deliberately-disabled state.
    /// </param>
    /// <param name="firefoxAddonId">Resolves the current Firefox add-on id (null when unconfigured).</param>
    /// <param name="repairFirefox">
    /// Re-applies the Firefox enterprise policy with the current tuning. Runs on a background
    /// thread; must be safe to call repeatedly and idempotent.
    /// </param>
    /// <param name="chromiumTarget">
    /// Returns the Chromium extension id + Web Store update URL when force-install is expected,
    /// or null (unpacked dev mode, disabled, or unconfigured) to skip the Chromium check.
    /// </param>
    public ExtensionPolicyWatchdog(
        Func<bool> shouldEnforce,
        Func<string?> firefoxAddonId,
        Action repairFirefox,
        Func<(string ExtensionId, string UpdateUrl)?> chromiumTarget,
        Action<string>? log = null)
    {
        _shouldEnforce = shouldEnforce;
        _firefoxAddonId = firefoxAddonId;
        _repairFirefox = repairFirefox;
        _chromiumTarget = chromiumTarget;
        _log = log;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _running)
                return;

            _running = true;
            SetupWatchers();
            _timer = new Timer(_ => SafeCheck(), null, TimeSpan.Zero, PollInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
            DisposeWatchers();
        }
    }

    private void SetupWatchers()
    {
        foreach (var root in FirefoxInstallRoots.All())
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;

                // Watch the whole install root (recursively) rather than distribution/ alone,
                // so deleting the distribution folder itself is still caught. FileName +
                // DirectoryName covers delete/create/rename of policies.json and the folder;
                // content-only edits are caught by the timer backstop.
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                };
                watcher.Deleted += OnFsEvent;
                watcher.Created += OnFsEvent;
                watcher.Renamed += OnFsRenamed;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch
            {
                // Best effort — the timer backstop still covers this root.
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        _watchers.Clear();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => SafeCheck();

    private void OnFsRenamed(object sender, RenamedEventArgs e) => SafeCheck();

    private void SafeCheck()
    {
        try
        {
            Check();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"policy watchdog error: {ex.Message}");
        }
    }

    private void Check()
    {
        lock (_gate)
        {
            if (_disposed || !_running)
                return;
        }

        if (!_shouldEnforce())
            return;

        // Both the Firefox policy file (under Program Files) and the Chromium forcelist (under
        // HKLM) are writable only by an elevated process; without admin the supervised user
        // can't touch them and there is nothing to repair.
        if (!HostsFileManager.IsRunningAsAdmin())
            return;

        var firefoxAddonId = _firefoxAddonId();
        var firefoxBroken = !string.IsNullOrWhiteSpace(firefoxAddonId)
            && !_firefox.IsForceInstallIntact(firefoxAddonId);

        var chromiumTarget = _chromiumTarget();
        var chromiumBroken = chromiumTarget is { } ct
            && !string.IsNullOrWhiteSpace(ct.ExtensionId)
            && !_chromium.IsForcelistIntact(ct.ExtensionId);

        if (!firefoxBroken && !chromiumBroken)
            return;

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastRepairUtc < MinRepairGap)
                return;
            _lastRepairUtc = now;
        }

        if (firefoxBroken)
        {
            AuditLog.Write(
                "SECURITY: Firefox extension policy (distribution/policies.json or bundled XPI) was " +
                "missing or tampered — restoring force-install.");
            _log?.Invoke("Firefox shield policy was removed — restoring it.");
            _repairFirefox();
        }

        if (chromiumBroken && chromiumTarget is { } target)
        {
            var roots = _chromium.RepairForcelist(target.ExtensionId, target.UpdateUrl);
            if (roots.Count > 0)
            {
                AuditLog.Write(
                    $"SECURITY: Chromium ExtensionInstallForcelist was removed for {roots.Count} browser(s) — " +
                    "restoring force-install.");
                _log?.Invoke("Browser shield policy was removed — restoring it.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Stop();
    }
}
