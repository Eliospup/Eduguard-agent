using EduGuardAgent.Models;

namespace EduGuardAgent.Agent;

internal sealed class SessionState
{
    private readonly Dictionary<string, AppBlockCategory> _blockedApps = new(StringComparer.OrdinalIgnoreCase);
    private int _suppressChanged;

    public event Action? Changed;

    public IReadOnlyCollection<string> BlockedApps => _blockedApps.Keys;

    public void BeginBulkUpdate() => Interlocked.Increment(ref _suppressChanged);

    public void EndBulkUpdate()
    {
        if (Interlocked.Decrement(ref _suppressChanged) == 0)
            Changed?.Invoke();
    }

    public void BlockApp(string name, AppBlockCategory category = AppBlockCategory.DomManual)
    {
        var normalized = NormalizeProcessName(name);
        var hadCategory = _blockedApps.TryGetValue(normalized, out var previousCategory);
        _blockedApps[normalized] = category;

        if (_suppressChanged == 0 && (!hadCategory || previousCategory != category))
            Changed?.Invoke();
    }

    public void UnblockApp(string name)
    {
        var normalized = NormalizeProcessName(name);
        if (_blockedApps.Remove(normalized) && _suppressChanged == 0)
            Changed?.Invoke();
    }

    public bool IsBlocked(string processName)
    {
        var normalized = NormalizeProcessName(processName);
        return _blockedApps.ContainsKey(normalized);
    }

    public bool TryGetBlockCategory(string processName, out AppBlockCategory category)
    {
        var normalized = NormalizeProcessName(processName);
        return _blockedApps.TryGetValue(normalized, out category);
    }

    public IEnumerable<string> GetBlockedAppsExcept(AppBlockCategory category) =>
        _blockedApps
            .Where(pair => pair.Value != category)
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeProcessName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.exe";
    }
}
