using System.Text.Json;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class UrlBlocklistStore
{
    private const string FileName = "blocked_hosts.json";

    private readonly HashSet<string> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public IReadOnlyCollection<string> BlockedHosts => _hosts;

    public void Load()
    {
        _hosts.Clear();

        var status = SecureStateFile.Read(FileName, out var json);
        if (status != StateReadStatus.Ok)
        {
            if (status == StateReadStatus.Tampered)
                AuditLog.Write("SECURITY: URL blocklist failed integrity check — starting empty (per-mode blocked hosts re-applied from the secured catalog).");
            return;
        }

        try
        {
            var hosts = JsonSerializer.Deserialize<List<string>>(json);
            if (hosts is null)
                return;

            foreach (var host in hosts)
            {
                var normalized = NormalizeHost(host);
                if (normalized is not null)
                    _hosts.Add(normalized);
            }
        }
        catch
        {
            // Corrupt store — start fresh.
        }
    }

    public bool Block(string host)
    {
        var normalized = NormalizeHost(host);
        if (normalized is null)
            return false;

        if (!_hosts.Add(normalized))
            return false;

        Save();
        Changed?.Invoke();
        return true;
    }

    public bool Unblock(string host)
    {
        var normalized = NormalizeHost(host);
        if (normalized is null)
            return false;

        if (!_hosts.Remove(normalized))
            return false;

        Save();
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_hosts.Count == 0)
            return;

        _hosts.Clear();
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_hosts.OrderBy(h => h).ToList());
        SecureStateFile.Write(FileName, json);
    }

    public static string? NormalizeHost(string host)
    {
        var trimmed = host.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("http://", StringComparison.Ordinal))
            trimmed = trimmed[7..];
        if (trimmed.StartsWith("https://", StringComparison.Ordinal))
            trimmed = trimmed[8..];

        var slash = trimmed.IndexOf('/');
        if (slash >= 0)
            trimmed = trimmed[..slash];

        var colon = trimmed.IndexOf(':');
        if (colon >= 0)
            trimmed = trimmed[..colon];

        if (trimmed.StartsWith("www.", StringComparison.Ordinal))
            trimmed = trimmed[4..];

        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains(' ') || trimmed.Contains(".."))
            return null;

        return trimmed;
    }

    public static IEnumerable<string> ExpandHostEntries(string host)
    {
        yield return host;
        yield return $"www.{host}";
    }
}
