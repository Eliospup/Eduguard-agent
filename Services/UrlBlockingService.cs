using System.Text.RegularExpressions;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class UrlBlockingService : IDisposable
{
    private readonly UrlBlocklistStore _store = new();
    private readonly HostsFileManager _hosts = new();
    private readonly BlockPageServer _server = new();
    private bool _blockingReleased;

    public event Action? Changed;

    public IReadOnlyCollection<string> BlockedHosts => _store.BlockedHosts;
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

            var canonical = _store.BlockedHosts
                .SelectMany(UrlBlocklistStore.ExpandHostEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return onDisk.Any(h => !canonical.Contains(h));
        }
    }

    public UrlBlockingService()
    {
        _store.Changed += () => Changed?.Invoke();
    }

    public void ReconcileFromDisk()
    {
        _store.Load();
        Apply();
        Changed?.Invoke();
    }

    public void Start()
    {
        ReconcileFromDisk();
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

        var result = _hosts.Clear();
        LastHostsError = result.Success ? null : result.Error;

        if (result.Success)
            AuditLog.Write("URL blocking released — hosts file restored.");
        else if (result.Error is not null)
            AuditLog.Write($"Failed to restore hosts file: {result.Error}");
    }

    public bool Apply()
    {
        var result = _hosts.ApplyBlocklist(_store.BlockedHosts);
        LastHostsError = result.Success ? null : result.Error;

        if (_store.BlockedHosts.Count == 0)
        {
            _server.Stop();
            if (!result.Success)
                AuditLog.Write($"Hosts update failed: {result.Error}");
            return result.Success;
        }

        _server.Stop();
        _server.Start(_store.BlockedHosts.ToList());

        if (!result.Success)
            AuditLog.Write($"Hosts update failed: {result.Error}");
        else
            AuditLog.Write($"URL blocklist applied ({_store.BlockedHosts.Count} hosts).");

        if (_server.LastError is not null)
            AuditLog.Write($"Block page server error: {_server.LastError}");

        return result.Success && (_server.IsRunning || _server.LastError is null);
    }

    private bool ApplyAndNotify()
    {
        var ok = Apply();
        Changed?.Invoke();
        return ok;
    }

    public void Dispose()
    {
        ReleaseBlocking();
        _server.Dispose();
    }
}
