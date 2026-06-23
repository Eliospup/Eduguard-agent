using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class SubProfileService
{
    private const int MaxNameLength = 32;

    private readonly SubProfileStore _store = new();
    private string? _displayName;

    public event Action? Changed;

    public bool HasDisplayName => !string.IsNullOrWhiteSpace(_displayName);

    public string? DisplayName => _displayName;

    public void LoadFromStorage()
    {
        var stored = _store.Load();
        _displayName = Normalize(stored.DisplayName);
    }

    public bool TrySetDisplayName(string? rawName)
    {
        var normalized = Normalize(rawName);
        if (normalized is null)
            return false;

        if (string.Equals(_displayName, normalized, StringComparison.Ordinal))
            return true;

        _displayName = normalized;
        _store.Save(new SubProfileStore.StoredSubProfile { DisplayName = normalized });
        Changed?.Invoke();
        return true;
    }

    public string ResolveDisplayName(ModeCopySet? tone = null)
    {
        if (!string.IsNullOrWhiteSpace(_displayName))
            return _displayName;

        return tone?.SubNameFallback ?? "sweetie";
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxNameLength)
            trimmed = trimmed[..MaxNameLength];

        if (!trimmed.Any(char.IsLetterOrDigit))
            return null;

        return trimmed;
    }
}
