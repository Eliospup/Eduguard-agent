using System.Text.RegularExpressions;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class UrlBlockingService : IDisposable
{
    private static readonly TimeSpan ReassertInterval = TimeSpan.FromSeconds(10);

    private readonly UrlBlocklistStore _store = new();
    private readonly HostsFileManager _hosts = new();
    private readonly BlockPageServer _server = new();
    private readonly object _hostsLock = new();
    private readonly HashSet<string> _categoryHosts = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _reassertTimer;
    private bool _blockingReleased;

    public event Action? Changed;

    /// <summary>User-managed blocked hosts (does not include category-filter domains).</summary>
    public IReadOnlyCollection<string> BlockedHosts => _store.BlockedHosts;

    /// <summary>
    /// Effective set actually written to the hosts file / served by the block page:
    /// user hosts plus category-filter domains.
    /// </summary>
    private IReadOnlyCollection<string> EffectiveHosts
    {
        get
        {
            if (_categoryHosts.Count == 0)
                return _store.BlockedHosts;

            var merged = new HashSet<string>(_store.BlockedHosts, StringComparer.OrdinalIgnoreCase);
            merged.UnionWith(_categoryHosts);
            return merged;
        }
    }
    public bool IsServerRunning => _server.IsRunning;
    public bool HasAdminRights => HostsFileManager.IsRunningAsAdmin();
    public string? LastHostsError { get; private set; }
    public string? LastServerError => _server.LastError ?? _server.CertificateError;

    public bool HasHostsOrphanEntries
    {
        get
        {
            var onDisk = _hosts.ReadManagedHosts();
            if (onDisk.Count == 0)
                return false;

            var canonical = EffectiveHosts
                .SelectMany(UrlBlocklistStore.ExpandHostEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return onDisk.Any(h => !canonical.Contains(h));
        }
    }

    public UrlBlockingService()
    {
        _store.Changed += () => Changed?.Invoke();
    }

    /// <summary>Keeps the block page's theme/copy in sync with the active supervision mode.</summary>
    public void SetMode(string displayName, Profiles.ModeTheme theme) => _server.SetMode(displayName, theme);

    public void ReconcileFromDisk()
    {
        _store.Load();
        Apply();
        Changed?.Invoke();
    }

    public void Start()
    {
        ReconcileFromDisk();
        // The hosts file is otherwise only written on demand, so an admin can delete the
        // managed section and it stays gone. Re-assert it on a tick (like the extension
        // policy guard) so the block survives manual edits while supervision is active.
        _reassertTimer ??= new Timer(_ => ReassertHosts(), null, ReassertInterval, ReassertInterval);
    }

    /// <summary>Restores the managed hosts section if it has drifted from the canonical blocklist.</summary>
    private void ReassertHosts()
    {
        try
        {
            lock (_hostsLock)
            {
                if (_blockingReleased || EffectiveHosts.Count == 0)
                    return;

                // Without admin the hosts file can't be written; the initial Apply already
                // surfaced that error, so skip silently instead of logging drift every tick.
                if (!HostsFileManager.IsRunningAsAdmin())
                    return;

                if (!HostsDrifted())
                    return;

                AuditLog.Write("SECURITY: hosts blocklist drift detected — re-applying managed section.");
                var result = _hosts.ApplyBlocklist(EffectiveHosts);
                LastHostsError = result.Success ? null : result.Error;
                if (!result.Success && result.Error is not null)
                    AuditLog.Write($"Hosts re-assert failed: {result.Error}");
            }
        }
        catch
        {
            // Best-effort; the next tick retries.
        }
    }

    private bool HostsDrifted()
    {
        var onDisk = _hosts.ReadManagedHosts().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canonical = EffectiveHosts
            .SelectMany(UrlBlocklistStore.ExpandHostEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return !onDisk.SetEquals(canonical);
    }

    /// <summary>
    /// Sets the category-filter domains (from enabled web-content categories). These are
    /// merged into the hosts file and block page but kept out of the user blocklist, so
    /// they never appear in the parent's site list and are fully removable by toggling the
    /// category off. Returns true if the effective set changed.
    /// </summary>
    public bool SetCategoryHosts(IEnumerable<string> hosts)
    {
        var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hosts)
        {
            var normalized = UrlBlocklistStore.NormalizeHost(host);
            if (normalized is not null)
                incoming.Add(normalized);
        }

        lock (_hostsLock)
        {
            if (_categoryHosts.SetEquals(incoming))
                return false;

            _categoryHosts.Clear();
            _categoryHosts.UnionWith(incoming);
        }

        ApplyAndNotify();
        return true;
    }

    public bool Block(string host)
    {
        if (!_store.Block(host))
            return false;

        return ApplyAndNotify();
    }

    public void BlockMany(IEnumerable<string> hosts)
    {
        var changed = false;
        foreach (var host in hosts)
        {
            if (_store.Block(host))
                changed = true;
        }

        if (changed)
            ApplyAndNotify();
    }

    public bool Unblock(string host)
    {
        if (!_store.Unblock(host))
            return false;

        return ApplyAndNotify();
    }

    public void UnblockMany(IEnumerable<string> hosts) =>
        UnblockMany(hosts, apply: true);

    public void UnblockManyWithoutApply(IEnumerable<string> hosts) =>
        UnblockMany(hosts, apply: false);

    private void UnblockMany(IEnumerable<string> hosts, bool apply)
    {
        var changed = false;
        foreach (var host in hosts)
        {
            if (_store.Unblock(host))
                changed = true;
        }

        if (changed && apply)
            ApplyAndNotify();
    }

    public void Clear()
    {
        _store.Clear();
        ReleaseBlocking();
        Changed?.Invoke();
        AuditLog.Write("URL blocklist cleared.");
    }

    /// <summary>
    /// Stops the block page server and restores the hosts file.
    /// Keeps the saved blocklist for the next supervised session.
    /// </summary>
    public void ReleaseBlocking()
    {
        if (_blockingReleased)
            return;

        _blockingReleased = true;
        _server.Stop();

        HostsUpdateResult result;
        lock (_hostsLock)
            result = _hosts.Clear();
        LastHostsError = result.Success ? null : result.Error;

        if (result.Success)
            AuditLog.Write("URL blocking released — hosts file restored.");
        else if (result.Error is not null)
            AuditLog.Write($"Failed to restore hosts file: {result.Error}");
    }

    public bool Apply()
    {
        lock (_hostsLock)
            return ApplyLocked();
    }

    private bool ApplyLocked()
    {
        var effective = EffectiveHosts;
        var result = _hosts.ApplyBlocklist(effective);
        LastHostsError = result.Success ? null : result.Error;

        if (effective.Count == 0)
        {
            _server.Stop();
            if (!result.Success)
                AuditLog.Write($"Hosts update failed: {result.Error}");
            return result.Success;
        }

        _server.Stop();
        _server.Start(effective.ToList());

        if (!result.Success)
            AuditLog.Write($"Hosts update failed: {result.Error}");
        else
            AuditLog.Write($"URL blocklist applied ({effective.Count} hosts).");

        if (_server.LastError is not null)
            AuditLog.Write($"Block page server error: {_server.LastError}");

        return result.Success && (_server.IsRunning || _server.LastError is null);
    }

    private bool ApplyAndNotify()
    {
        // Apply() does hosts-file I/O, X.509 leaf-certificate generation for any newly
        // blocked host, and a full Kestrel restart — easily seconds of work once a whole
        // content category's worth of domains is added at once. All the callers here run
        // synchronously on the UI thread (a settings toggle/button click), so push the work
        // to a background thread instead of freezing the window on every edit. The Changed
        // event's only subscriber already marshals back to the UI thread itself.
        Task.Run(() =>
        {
            Apply();
            Changed?.Invoke();
        });
        return true;
    }

    public void Dispose()
    {
        _reassertTimer?.Dispose();
        _reassertTimer = null;
        ReleaseBlocking();
        _server.Dispose();
    }
}
