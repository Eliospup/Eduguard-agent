using System.Text.RegularExpressions;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class GamingGameRegistry
{
    private static readonly Regex ExeFormat = new(@"^[a-z0-9 ._-]+\.exe$", RegexOptions.CultureInvariant);

    private readonly GamingSettingsStore _store = new();
    private readonly Dictionary<string, string> _extraGames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredGames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _gameLimits = new(StringComparer.OrdinalIgnoreCase);

    public void LoadFromStorage()
    {
        var stored = _store.Load();
        _extraGames.Clear();
        _ignoredGames.Clear();

        if (stored.ExtraGames is not null)
        {
            foreach (var game in stored.ExtraGames)
            {
                if (!TryNormalizeExe(game.Exe, out var exe) || string.IsNullOrWhiteSpace(game.Name))
                    continue;

                _extraGames[exe] = game.Name.Trim();
            }
        }

        if (stored.IgnoredGames is not null)
        {
            foreach (var exe in stored.IgnoredGames)
            {
                if (TryNormalizeExe(exe, out var normalized))
                    _ignoredGames.Add(normalized);
            }
        }

        _gameLimits.Clear();
        if (stored.GameLimits is not null)
        {
            foreach (var pair in stored.GameLimits)
                TrySetLimit(pair.Key, pair.Value);
        }
    }

    public void ApplySettings(GamingSettingsPayload settings, bool replaceGameLists = false)
    {
        var changed = false;

        if (settings.ExtraGames is not null
            && (replaceGameLists || settings.ExtraGames.Count > 0))
        {
            _extraGames.Clear();
            foreach (var game in settings.ExtraGames)
            {
                if (!TryNormalizeExe(game.Exe, out var exe) || string.IsNullOrWhiteSpace(game.Name))
                    continue;

                _extraGames[exe] = game.Name.Trim();
            }

            changed = true;
        }

        if (settings.IgnoredGames is not null
            && (replaceGameLists || settings.IgnoredGames.Count > 0))
        {
            _ignoredGames.Clear();
            foreach (var exe in settings.IgnoredGames)
            {
                if (TryNormalizeExe(exe, out var normalized))
                    _ignoredGames.Add(normalized);
            }

            changed = true;
        }

        if (settings.GameLimits is not null
            && (replaceGameLists || settings.GameLimits.Count > 0))
        {
            if (replaceGameLists)
                _gameLimits.Clear();

            foreach (var pair in settings.GameLimits)
                TrySetLimit(pair.Key, pair.Value);

            changed = true;
        }

        if (changed)
            Persist();
    }

    public bool AddExtraGame(string exe, string name)
    {
        if (!TryNormalizeExe(exe, out var normalized) || string.IsNullOrWhiteSpace(name))
            return false;

        _extraGames[normalized] = name.Trim();
        Persist();
        return true;
    }

    public bool RemoveExtraGame(string exe)
    {
        if (!TryNormalizeExe(exe, out var normalized))
            return false;

        if (!_extraGames.Remove(normalized))
            return false;

        Persist();
        return true;
    }

    public IReadOnlyList<(string Exe, string Name)> GetExtraGames() =>
        _extraGames
            .Select(p => (p.Key, p.Value))
            .OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyDictionary<string, int> GameLimits => _gameLimits;

    public int? GetLimitMinutes(string exe)
    {
        if (!TryNormalizeExe(exe, out var normalized))
            return null;

        return _gameLimits.TryGetValue(normalized, out var minutes) && minutes > 0
            ? minutes
            : null;
    }

    public string ResolveDisplayName(string exe)
    {
        if (TryGetExtraName(exe, out var extra))
            return extra;

        return GameCatalog.ResolveName(exe);
    }

    private bool TrySetLimit(string exe, int minutes)
    {
        if (!TryNormalizeExe(exe, out var normalized))
            return false;

        if (minutes <= 0)
        {
            _gameLimits.Remove(normalized);
            return true;
        }

        if (minutes > 1440)
            minutes = 1440;

        _gameLimits[normalized] = minutes;
        return true;
    }

    public bool IsIgnored(string exe)
    {
        if (TryNormalizeExe(exe, out var normalized))
            return _ignoredGames.Contains(normalized);

        return _ignoredGames.Contains(exe.Trim().ToLowerInvariant());
    }

    public bool TryGetExtraName(string exe, out string name)
    {
        var normalized = NormalizeExe(exe);
        return _extraGames.TryGetValue(normalized, out name!);
    }

    public bool IsExtraGame(string exe) => TryGetExtraName(exe, out _);

    public static bool IsValidExe(string? exe) =>
        !string.IsNullOrWhiteSpace(exe) && TryNormalizeExe(exe, out _);

    public static bool TryNormalizeExe(string? exe, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(exe))
            return false;

        var trimmed = exe.Trim().ToLowerInvariant();
        if (!trimmed.EndsWith(".exe", StringComparison.Ordinal))
            trimmed += ".exe";

        if (!ExeFormat.IsMatch(trimmed))
            return false;

        normalized = trimmed;
        return true;
    }

    public void PreserveInStored(StoredGamingSettings stored)
    {
        stored.ExtraGames = _extraGames
            .Select(pair => new StoredGamingExtraGame { Exe = pair.Key, Name = pair.Value })
            .OrderBy(g => g.Exe, StringComparer.OrdinalIgnoreCase)
            .ToList();
        stored.IgnoredGames = _ignoredGames
            .OrderBy(exe => exe, StringComparer.OrdinalIgnoreCase)
            .ToList();
        stored.GameLimits = _gameLimits.Count == 0
            ? null
            : new Dictionary<string, int>(_gameLimits, StringComparer.OrdinalIgnoreCase);
    }

    private void Persist()
    {
        var stored = _store.Load();
        PreserveInStored(stored);
        _store.Save(stored);
    }

    private static string NormalizeExe(string exe) =>
        TryNormalizeExe(exe, out var normalized) ? normalized : exe.Trim().ToLowerInvariant();
}
