using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed record KioskApp(string Name, string Path, string? Args, string? Icon)
{
    public string NormalizedPath => NormalizePath(Path);

    public static string NormalizePath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path.Trim()).ToLowerInvariant();
        }
        catch
        {
            return path.Trim().ToLowerInvariant();
        }
    }
}

/// <summary>
/// Holds the list of apps the Dom approved for kiosk mode. The list is synced from the
/// server (heartbeat / <c>set_kiosk_apps</c> command) and persisted locally so the kiosk
/// keeps working offline and across restarts.
/// </summary>
internal sealed class KioskAppRegistry
{
    private readonly KioskSettingsStore _store = new();
    private readonly object _lock = new();
    private List<KioskApp> _apps = [];

    public event Action? Changed;

    public KioskAppRegistry()
    {
        LoadFromStorage();
        SeedDefaultsIfEmpty();
        MergeNewlyDiscoveredDefaults();
    }

    public IReadOnlyList<KioskApp> Apps
    {
        get
        {
            lock (_lock)
                return _apps.ToArray();
        }
    }

    public bool IsApprovedPath(string fullPath)
    {
        var normalized = KioskApp.NormalizePath(fullPath);
        lock (_lock)
            return _apps.Any(a => string.Equals(a.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsApprovedProcessName(string processName)
    {
        lock (_lock)
            return _apps.Any(a =>
                string.Equals(
                    System.IO.Path.GetFileNameWithoutExtension(a.Path),
                    processName,
                    StringComparison.OrdinalIgnoreCase));
    }

    public void SetApprovedApps(IEnumerable<KioskApp> apps)
    {
        var parsed = apps
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && IsValidPath(a.Path))
            .Select(a => new KioskApp(a.Name.Trim(), a.Path.Trim(), Clean(a.Args), Clean(a.Icon)))
            .ToList();

        lock (_lock)
            _apps = parsed;

        Persist();
        Changed?.Invoke();
    }

    public void MergeNewlyDiscoveredDefaults()
    {
        var discovered = KioskAppDiscovery.DiscoverAll();
        var added = false;

        lock (_lock)
        {
            var existing = new HashSet<string>(
                _apps.Select(a => a.NormalizedPath),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in discovered)
            {
                var normalized = KioskApp.NormalizePath(item.Path);
                if (existing.Contains(normalized))
                    continue;

                _apps.Add(new KioskApp(item.Name, item.Path, Clean(item.Args), Clean(item.Icon)));
                existing.Add(normalized);
                added = true;
            }
        }

        if (!added)
            return;

        Persist();
        Changed?.Invoke();
    }

    public void ApplySettings(KioskSettingsPayload payload)
    {
        if (payload.ApprovedApps is null)
            return;

        var parsed = payload.ApprovedApps
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && IsValidPath(a.Path))
            .Select(a => new KioskApp(a.Name.Trim(), a.Path.Trim(), Clean(a.Args), Clean(a.Icon)))
            .ToList();

        lock (_lock)
            _apps = parsed;

        Persist();
        Changed?.Invoke();
    }

    private void SeedDefaultsIfEmpty()
    {
        List<KioskApp> seeded;
        lock (_lock)
        {
            if (_apps.Count > 0)
                return;

            var discovered = KioskAppDiscovery.DiscoverAll();
            if (discovered.Count == 0)
                return;

            seeded = discovered
                .Select(d => new KioskApp(d.Name, d.Path, Clean(d.Args), Clean(d.Icon)))
                .ToList();
            _apps = seeded;
        }

        Persist();
        Changed?.Invoke();
    }

    private void LoadFromStorage()
    {
        var stored = _store.Load();
        _apps = stored.ApprovedApps
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && IsValidPath(a.Path))
            .Select(a => new KioskApp(a.Name!.Trim(), a.Path!.Trim(), Clean(a.Args), Clean(a.Icon)))
            .ToList();
    }

    private void Persist()
    {
        StoredKioskSettings stored;
        lock (_lock)
        {
            stored = new StoredKioskSettings
            {
                ApprovedApps = _apps
                    .Select(a => new StoredKioskApp
                    {
                        Name = a.Name,
                        Path = a.Path,
                        Args = a.Args,
                        Icon = a.Icon,
                    })
                    .ToList(),
            };
        }

        _store.Save(stored);
    }

    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
