using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Category-based web filtering. Enabled categories map to (1) curated domains pushed to
/// the hosts file / block page (DNS-level, every browser) and (2) hostname keyword tokens
/// pushed to the extension so it can auto-categorise sites that aren't in the curated list.
/// These are safety categories — always hard, regardless of supervision mode.
/// </summary>
internal sealed class WebContentFilterService
{
    private readonly WebCategorySettingsStore _store = new();
    private readonly UrlBlockingService _urlBlocking;
    private readonly FamilyDnsShieldService _dnsShield = new();
    private readonly object _lock = new();
    private HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);

    public WebContentFilterService(UrlBlockingService urlBlocking)
    {
        _urlBlocking = urlBlocking;
    }

    public bool IsDnsShieldActive => _dnsShield.IsEnabled;

    public event Action? Changed;

    public IReadOnlyCollection<string> EnabledCategoryKeys
    {
        get
        {
            lock (_lock)
                return _enabled.ToList();
        }
    }

    public bool IsEnabled(string key)
    {
        lock (_lock)
            return _enabled.Contains(key);
    }

    /// <summary>Keyword tokens for the extension's hostname heuristic (auto-categorisation).</summary>
    public IReadOnlyList<string> EnabledKeywords
    {
        get
        {
            lock (_lock)
                return WebCategoryCatalog.KeywordsFor(_enabled);
        }
    }

    /// <summary>Weighted page-content vocabularies for the extension's on-device text scoring.</summary>
    public IReadOnlyDictionary<string, object> EnabledContentTerms
    {
        get
        {
            lock (_lock)
                return WebCategoryCatalog.ContentTermsFor(_enabled);
        }
    }

    /// <summary>Loads persisted categories and applies their domains to the hosts blocklist.</summary>
    public void LoadAndApply()
    {
        // A backup left behind while "adult" is off means a previous session crashed
        // mid-shield — restore the user's DNS before (re)deciding what to enable.
        var loaded = _store.Load();
        lock (_lock)
            _enabled = loaded;

        if (!loaded.Contains("adult"))
            _dnsShield.TryReleaseOrphanedState();

        ApplyDomains();
        SyncDnsShield();
    }

    /// <summary>Reversibility on app shutdown: restores DNS without touching saved settings.</summary>
    public void ReleaseEnforcement() => _dnsShield.Release();

    /// <summary>Replaces the enabled set (from settings UI), persists and re-applies.</summary>
    public void SetEnabledCategories(IEnumerable<string> keys)
    {
        var next = new HashSet<string>(
            keys.Where(WebCategoryCatalog.IsKnown),
            StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            if (_enabled.SetEquals(next))
                return;
            _enabled = next;
        }

        _store.Save(next);
        ApplyDomains();
        SyncDnsShield();
        AuditLog.Write($"Web content filtering categories updated ({next.Count} active).");
        Changed?.Invoke();
    }

    private void ApplyDomains()
    {
        IReadOnlyList<string> domains;
        lock (_lock)
            domains = WebCategoryCatalog.DomainsFor(_enabled);
        _urlBlocking.SetCategoryHosts(domains);
    }

    /// <summary>
    /// The adaptive layer: "adult" also routes system DNS through Cloudflare Family, whose
    /// continuously updated categorisation covers the long tail no curated list can. The
    /// netsh/policy work runs off-thread — callers are UI-thread toggles.
    /// </summary>
    private void SyncDnsShield()
    {
        bool wantDns;
        lock (_lock)
            wantDns = _enabled.Contains("adult");

        Task.Run(() =>
        {
            if (wantDns)
                _dnsShield.Enable();
            else
                _dnsShield.Release();
        });
    }
}
