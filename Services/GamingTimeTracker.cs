using System.Diagnostics;
using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal readonly record struct GameUsageSnapshot(string Key, string DisplayName, TimeSpan Usage);

internal sealed class GamingTimeTracker : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxSampleGap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoticeCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StudyNoticeCooldown = TimeSpan.FromSeconds(15);

    private readonly DispatcherTimer _timer;
    private readonly GamingUsageStore _store = new();
    private readonly GamingSettingsStore _settingsStore = new();
    private readonly GamingGameRegistry _games = new();
    private readonly StudyTimeService _studyTime;
    private readonly Dictionary<string, double> _perGameSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    private double _globalSeconds;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private WeeklyMinuteLimits _limits = WeeklyMinuteLimits.Create(
        Config.TestingShortGamingTime
            ? 2
            : AgentModeRegistry.Sub.Defaults.GamingTimeLimitMinutes);
    private int _limitMinutes;
    private bool _showPlaytimeOverlay = true;
    private bool _enforcementEnabled;
    private DateTimeOffset _lastLimitNotice = DateTimeOffset.MinValue;
    private readonly Dictionary<string, DateTimeOffset> _lastStudyNoticeByTarget =
        new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastPersist = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRunningScan = DateTimeOffset.MinValue;
    private string? _lastCountedGameKey;
    private string? _lastForegroundGameKey;
    private HashSet<string> _runningGameKeys = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DetectedGame> _cachedRunningGames = [];
    private volatile bool _runningScanInFlight;

    private readonly Func<bool> _isHardEnforcement;

    public GamingTimeTracker(Dispatcher dispatcher, StudyTimeService studyTime, Func<bool>? isHardEnforcement = null)
    {
        _isHardEnforcement = isHardEnforcement ?? (() => true);
        _studyTime = studyTime;
        _timer = new DispatcherTimer(SampleInterval, DispatcherPriority.Normal, OnTick, dispatcher);
        _games.LoadFromStorage();
        GameCatalog.Bind(_games);
        LoadFromStore();
        LoadSettingsFromStorage();
        RefreshTodayLimit();
        PurgeIgnoredUsage();
    }

    public event Action? UsageChanged;
    public event Action<string>? LimitReached;
    public event Action<string>? StudyModeBlocked;
    public event Action<GamingHudState?>? HudStateChanged;
    public event Action<DetectedGame>? GameSessionStarted;

    public bool AddExtraGame(string exe, string name) => _games.AddExtraGame(exe, name);
    public bool RemoveExtraGame(string exe) => _games.RemoveExtraGame(exe);
    public IReadOnlyList<(string Exe, string Name)> GetExtraGames() => _games.GetExtraGames();

    public int LimitMinutes => _limitMinutes;

    public bool ShowPlaytimeOverlay => _showPlaytimeOverlay;

    public TimeSpan TotalUsage
    {
        get
        {
            lock (_stateLock)
                return TimeSpan.FromSeconds(_globalSeconds);
        }
    }

    public TimeSpan LimitDuration => TimeSpan.FromMinutes(_limitMinutes);

    public double LimitSeconds => _limitMinutes * 60.0;

    public bool IsOverLimit
    {
        get
        {
            lock (_stateLock)
                return _globalSeconds >= LimitSeconds;
        }
    }

    public double Progress
    {
        get
        {
            lock (_stateLock)
            {
                return LimitSeconds <= 0
                    ? 0
                    : Math.Min(_globalSeconds / LimitSeconds * 100.0, 100.0);
            }
        }
    }

    public void Start()
    {
        _enforcementEnabled = true;
        _lastSampleAt = DateTimeOffset.UtcNow;
        _timer.Start();
        UsageChanged?.Invoke();
    }

    public void Stop()
    {
        _enforcementEnabled = false;
        _timer.Stop();
        _lastCountedGameKey = null;
        _lastForegroundGameKey = null;
        _runningGameKeys.Clear();
        Persist();
        HudStateChanged?.Invoke(null);
    }

    public void SetLimitMinutes(int minutes) =>
        ApplyLimits(WeeklyMinuteLimits.Parse(minutes, null, _limits, _limits.DefaultMinutes));

    public void ApplyWeeklyLimits(WeeklyMinuteLimits limits) => ApplyLimits(limits);

    private void ApplyLimits(WeeklyMinuteLimits limits)
    {
        if (limits.ScheduleKey == _limits.ScheduleKey)
            return;

        _limits = limits;
        RefreshTodayLimit();
        PersistSettings();
        UsageChanged?.Invoke();
    }

    private void RefreshTodayLimit() =>
        _limitMinutes = _limits.ForDate(_today);

    public void SetShowPlaytimeOverlay(bool show)
    {
        if (_showPlaytimeOverlay == show)
            return;

        _showPlaytimeOverlay = show;
        PersistSettings();

        if (!show)
            HudStateChanged?.Invoke(null);
    }

    public void ApplySettings(GamingSettingsPayload settings, bool replaceGameLists = false)
    {
        if (settings.DailyLimitMinutes is not null || settings.WeeklyLimits is not null)
        {
            ApplyLimits(WeeklyMinuteLimits.Parse(
                settings.DailyLimitMinutes,
                settings.WeeklyLimits,
                _limits,
                _limits.DefaultMinutes));
        }

        if (settings.ShowPlaytimeOverlay is { } show)
            SetShowPlaytimeOverlay(show);

        if (settings.ExtraGames is not null || settings.IgnoredGames is not null)
            _games.ApplySettings(settings, replaceGameLists);

        PurgeIgnoredUsage();
    }

    public void ApplyUsageTo(HeartbeatRequest request)
    {
        request.GamingUsage = BuildHeartbeatUsage();
    }

    public GamingUsagePayload BuildHeartbeatUsage()
    {
        lock (_stateLock)
        {
            var breakdown = GetBreakdownLocked();
            return new GamingUsagePayload
            {
                Date = _today.ToString("yyyy-MM-dd"),
                TotalSeconds = (int)Math.Round(_globalSeconds),
                LimitMinutes = _limitMinutes,
                Games = breakdown
                    .Select(game => new GamingUsageGamePayload
                    {
                        Key = game.Key,
                        Name = game.DisplayName,
                        Seconds = (int)Math.Round(game.Usage.TotalSeconds),
                    })
                    .ToList(),
            };
        }
    }

    public IReadOnlyList<GameUsageSnapshot> GetBreakdown()
    {
        lock (_stateLock)
            return GetBreakdownLocked();
    }

    private IReadOnlyList<GameUsageSnapshot> GetBreakdownLocked() =>
        _perGameSeconds
            .Where(pair => !string.IsNullOrEmpty(pair.Key) && !_games.IsIgnored(pair.Key))
            .Select(pair => new GameUsageSnapshot(
                pair.Key,
                _displayNames.TryGetValue(pair.Key, out var name) ? name : GameCatalog.ResolveName(pair.Key),
                TimeSpan.FromSeconds(pair.Value)))
            .OrderByDescending(g => g.Usage)
            .ToList();

    public void Dispose()
    {
        _timer.Stop();
        Persist();
        HudStateChanged?.Invoke(null);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = ComputeElapsedSeconds(now);
        _lastSampleAt = now;

        var previousRunningGameKeys = _runningGameKeys;
        RefreshRunningGamesIfDue(now, TimeSpan.FromSeconds(2));
        _runningGameKeys = _cachedRunningGames
            .Select(game => game.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        DetectedGame? foregroundGame = null;
        if (ForegroundWindowDetector.TryGetForegroundProcessExe(out var foregroundExe)
            && GameCatalog.TryResolveForegroundGame(foregroundExe, out var resolvedForeground))
        {
            foregroundGame = resolvedForeground;
        }

        var shouldPersist = false;
        var shouldEnforceGlobalLimit = false;
        DetectedGame? activeGame = null;
        string? countedGameKey = null;
        var previousForegroundGameKey = _lastForegroundGameKey;

        if (_enforcementEnabled && _studyTime.IsActiveNow && _studyTime.Settings.BlockGames)
        {
            if (_cachedRunningGames.Count > 0)
                EnforceStudyMode(_cachedRunningGames);

            _lastForegroundGameKey = foregroundGame?.Key;
            RaiseHudState(null);
            UsageChanged?.Invoke();
            return;
        }

        lock (_stateLock)
        {
            RolloverIfNewDayLocked();

            if (foregroundGame is { } foreground
                && elapsedSeconds > 0)
            {
                activeGame = foreground;
                countedGameKey = foreground.Key;
                _globalSeconds += elapsedSeconds;

                _perGameSeconds.TryGetValue(foreground.Key, out var seconds);
                _perGameSeconds[foreground.Key] = seconds + elapsedSeconds;
                _displayNames[foreground.Key] = foreground.DisplayName;

                shouldPersist = DateTimeOffset.UtcNow - _lastPersist >= PersistInterval;

                if (_enforcementEnabled && _globalSeconds >= LimitSeconds)
                    shouldEnforceGlobalLimit = true;
            }
        }

        if (_lastCountedGameKey is not null && countedGameKey != _lastCountedGameKey)
            shouldPersist = true;

        _lastCountedGameKey = countedGameKey;

        if (ShouldAnnounceGameSession(
                previousForegroundGameKey,
                foregroundGame?.Key,
                foregroundGame,
                previousRunningGameKeys))
        {
            GameSessionStarted?.Invoke(foregroundGame!.Value);
        }

        _lastForegroundGameKey = foregroundGame?.Key;

        if (shouldPersist)
            Persist();

        if (shouldEnforceGlobalLimit && activeGame is { } globalLimited)
            EnforceLimit([globalLimited]);

        RaiseHudState(foregroundGame);
        UsageChanged?.Invoke();
    }

    private double ComputeElapsedSeconds(DateTimeOffset now)
    {
        if (_lastSampleAt == DateTimeOffset.MinValue)
            return SampleInterval.TotalSeconds;

        var elapsed = now - _lastSampleAt;
        if (elapsed <= TimeSpan.Zero)
            return 0;

        if (elapsed > MaxSampleGap)
            return MaxSampleGap.TotalSeconds;

        return elapsed.TotalSeconds;
    }

    /// <summary>
    /// Triggers a background process-list scan when due. Process.GetProcesses() is a full
    /// system enumeration (can take tens of ms) — running it on the dispatcher thread every
    /// tick caused visible UI stutter (e.g. janky scrolling). The scan runs on the thread
    /// pool and the cache is swapped in when it completes; the tick that requested it keeps
    /// using the previous snapshot in the meantime.
    /// </summary>
    private void RefreshRunningGamesIfDue(DateTimeOffset now, TimeSpan interval)
    {
        if (_lastRunningScan != DateTimeOffset.MinValue && now - _lastRunningScan < interval)
            return;

        if (_runningScanInFlight)
            return;

        _runningScanInFlight = true;
        _lastRunningScan = now;
        Task.Run(() =>
        {
            try
            {
                _cachedRunningGames = GameCatalog.GetRunningGames();
            }
            finally
            {
                _runningScanInFlight = false;
            }
        });
    }

    private bool ShouldAnnounceGameSession(
        string? previousForegroundKey,
        string? currentForegroundKey,
        DetectedGame? foregroundGame,
        HashSet<string> previousRunningGameKeys)
    {
        if (!_enforcementEnabled
            || currentForegroundKey is null
            || foregroundGame is null
            || currentForegroundKey == previousForegroundKey)
        {
            return false;
        }

        lock (_stateLock)
        {
            if (_limitMinutes <= 0 || _globalSeconds >= LimitSeconds)
                return false;
        }

        if (previousForegroundKey is null)
        {
            if (previousRunningGameKeys.Count == 0)
                return true;

            return !previousRunningGameKeys.Contains(currentForegroundKey);
        }

        if (previousRunningGameKeys.Contains(currentForegroundKey))
            return false;

        return true;
    }

    private void RaiseHudState(DetectedGame? foregroundGame)
    {
        if (!_showPlaytimeOverlay
            || !_enforcementEnabled
            || foregroundGame is not { } game
            || string.IsNullOrEmpty(game.Key))
        {
            HudStateChanged?.Invoke(null);
            return;
        }

        GamingHudState hudState;
        lock (_stateLock)
        {
            var globalRemaining = LimitDuration - TimeSpan.FromSeconds(_globalSeconds);
            var exhausted = globalRemaining <= TimeSpan.Zero;

            var progressBase = LimitSeconds <= 0
                ? 0
                : Math.Min(_globalSeconds / LimitSeconds * 100.0, 100.0);

            hudState = new GamingHudState
            {
                GameName = game.DisplayName,
                RemainingLabel = exhausted ? UiCopy.HudTimesUpLabel : FormatCountdown(globalRemaining),
                Progress = Math.Min(progressBase, 100.0),
            };
        }

        HudStateChanged?.Invoke(hudState);
    }

    private void EnforceLimit(IReadOnlyList<DetectedGame> running)
    {
        if (_isHardEnforcement())
        {
            if (!TryKillGames(running, out _))
                return;
        }
        else if (running.Count == 0)
        {
            return; // Soft mode: nothing running to remind about.
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastLimitNotice < NoticeCooldown)
            return;

        _lastLimitNotice = now;
        LimitReached?.Invoke(FormatDuration(LimitDuration));
    }

    private void EnforceStudyMode(IReadOnlyList<DetectedGame> running)
    {
        string key;
        if (_isHardEnforcement())
        {
            if (!TryKillGames(running, out var closedName))
                return;
            key = closedName!;
        }
        else
        {
            if (running.Count == 0)
                return; // Soft mode: remind without closing.
            key = running[0].DisplayName;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastStudyNoticeByTarget.TryGetValue(key, out var last) && now - last < StudyNoticeCooldown)
            return;

        _lastStudyNoticeByTarget[key] = now;
        StudyModeBlocked?.Invoke(key);
    }

    public void KillRunningGames()
    {
        IReadOnlyList<DetectedGame> snapshot;
        lock (_stateLock)
            snapshot = _cachedRunningGames;
        TryKillGames(snapshot, out _);
        // Reset cooldown so overlay fires immediately if the game is relaunched.
        _lastLimitNotice = DateTimeOffset.MinValue;
    }

    private static bool TryKillGames(IReadOnlyList<DetectedGame> running, out string? closedDisplayName)
    {
        closedDisplayName = null;
        var closedAny = false;

        foreach (var game in running)
        {
            var processName = game.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? game.Key[..^4]
                : game.Key;

            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    closedAny = true;
                    closedDisplayName ??= game.DisplayName;
                }
                catch
                {
                    // Some processes may refuse termination.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return closedAny;
    }

    private void RolloverIfNewDayLocked()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _today)
            return;

        _today = today;
        _globalSeconds = 0;
        _perGameSeconds.Clear();
        _displayNames.Clear();
        _lastLimitNotice = DateTimeOffset.MinValue;
        _lastPersist = DateTimeOffset.MinValue;
        RefreshTodayLimit();
        PersistLocked();
    }

    private void LoadFromStore()
    {
        var stored = _store.Load(_today);
        _globalSeconds = stored.GlobalSeconds;
        if (stored.Games is not null)
        {
            foreach (var pair in stored.Games)
            {
                if (string.IsNullOrEmpty(pair.Key))
                    continue;

                _perGameSeconds[pair.Key] = pair.Value;
            }
        }
    }

    private void LoadSettingsFromStorage()
    {
        var settings = _settingsStore.Load();
        _limits = WeeklyMinuteLimits.Parse(
            settings.DailyLimitMinutes,
            settings.WeeklyLimits,
            null,
            Config.TestingShortGamingTime
                ? 2
                : AgentModeRegistry.Sub.Defaults.GamingTimeLimitMinutes);

        if (settings.ShowPlaytimeOverlay is { } show)
            _showPlaytimeOverlay = show;
    }

    private void PersistSettings()
    {
        var stored = _settingsStore.Load();
        stored.DailyLimitMinutes = _limits.DefaultMinutes;
        stored.WeeklyLimits = _limits.HasOverrides
            ? WeeklyMinuteLimits.SerializeDays(_limits.DayMinutes)
            : null;
        stored.ShowPlaytimeOverlay = _showPlaytimeOverlay;
        _games.PreserveInStored(stored);
        _settingsStore.Save(stored);
    }

    private void PurgeIgnoredUsage()
    {
        lock (_stateLock)
        {
            var removedSeconds = 0.0;
            var toRemove = new List<string>();

            foreach (var pair in _perGameSeconds)
            {
                if (!_games.IsIgnored(pair.Key))
                    continue;

                removedSeconds += pair.Value;
                toRemove.Add(pair.Key);
            }

            if (toRemove.Count == 0)
                return;

            foreach (var key in toRemove)
            {
                _perGameSeconds.Remove(key);
                _displayNames.Remove(key);
            }

            var maxRemaining = _perGameSeconds.Values.DefaultIfEmpty(0).Max();
            _globalSeconds = Math.Max(maxRemaining, _globalSeconds - removedSeconds);
            PersistLocked();
        }

        UsageChanged?.Invoke();
    }

    private void Persist()
    {
        lock (_stateLock)
            PersistLocked();
    }

    private void PersistLocked()
    {
        _store.Save(new StoredGamingUsage
        {
            Date = _today.ToString("yyyy-MM-dd"),
            GlobalSeconds = _globalSeconds,
            Games = new Dictionary<string, double>(_perGameSeconds, StringComparer.OrdinalIgnoreCase),
        });

        _lastPersist = DateTimeOffset.UtcNow;
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

        return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "0m";

        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        return $"{(int)Math.Ceiling(duration.TotalMinutes)}m";
    }
}
