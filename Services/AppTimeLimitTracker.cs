using System.Diagnostics;
using System.Windows.Threading;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class AppTimeLimitDisplayRow
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public TimeSpan Usage { get; init; }
    public int LimitMinutes { get; init; }
    public bool IsExhausted => Usage >= TimeSpan.FromMinutes(LimitMinutes);

    public TimeSpan Remaining
    {
        get
        {
            var remaining = TimeSpan.FromMinutes(LimitMinutes) - Usage;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public double Progress =>
        LimitMinutes <= 0
            ? 0
            : Math.Min(Usage.TotalMinutes / LimitMinutes * 100.0, 100.0);
}

internal sealed class AppTimeLimitTracker : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxSampleGap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoticeCooldown = TimeSpan.FromMinutes(1);

    private readonly DispatcherTimer _timer;
    private readonly AppTimeUsageStore _store = new();
    private readonly Dictionary<string, int> _limits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _usageSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private bool _trackingEnabled;
    private DateTimeOffset _lastLimitNotice = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPersist = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.MinValue;

    private readonly Func<bool> _isHardEnforcement;

    public AppTimeLimitTracker(Dispatcher dispatcher, Func<bool>? isHardEnforcement = null)
    {
        _isHardEnforcement = isHardEnforcement ?? (() => true);
        _timer = new DispatcherTimer(SampleInterval, DispatcherPriority.Normal, OnTick, dispatcher);
        LoadFromStore();
    }

    public event Action? UsageChanged;
    public event Action<string, string>? LimitReached;

    public bool HasLimits
    {
        get
        {
            lock (_stateLock)
                return _limits.Count > 0;
        }
    }

    public void ApplyLimits(Dictionary<string, int>? limits)
    {
        lock (_stateLock)
        {
            _limits.Clear();
            if (limits is not null)
            {
                foreach (var pair in limits)
                {
                    if (!GamingGameRegistry.TryNormalizeExe(pair.Key, out var exe))
                        continue;

                    if (pair.Value <= 0)
                        continue;

                    _limits[exe] = Math.Min(pair.Value, 1440);
                }
            }

            PurgeOrphanUsageLocked();
        }

        UsageChanged?.Invoke();
    }

    public IReadOnlyList<AppTimeLimitDisplayRow> GetDisplayRows()
    {
        lock (_stateLock)
        {
            var rows = new List<AppTimeLimitDisplayRow>(_limits.Count);
            foreach (var pair in _limits.OrderBy(p => AppDisplayNames.Resolve(p.Key), StringComparer.OrdinalIgnoreCase))
            {
                _usageSeconds.TryGetValue(pair.Key, out var seconds);
                rows.Add(new AppTimeLimitDisplayRow
                {
                    Key = pair.Key,
                    DisplayName = AppDisplayNames.Resolve(pair.Key),
                    Usage = TimeSpan.FromSeconds(seconds),
                    LimitMinutes = pair.Value,
                });
            }

            return rows;
        }
    }

    public void Start()
    {
        _trackingEnabled = true;
        _lastSampleAt = DateTimeOffset.UtcNow;
        _timer.Start();
        UsageChanged?.Invoke();
    }

    public void Stop()
    {
        _trackingEnabled = false;
        _timer.Stop();
        Persist();
    }

    public void Dispose()
    {
        _timer.Stop();
        Persist();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_trackingEnabled)
            return;

        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = ComputeElapsedSeconds(now);
        _lastSampleAt = now;

        if (!ForegroundWindowDetector.TryGetForegroundProcessExe(out var foregroundExe)
            || !GamingGameRegistry.TryNormalizeExe(foregroundExe, out var exe))
        {
            return;
        }

        string? limitedExe = null;
        var shouldEnforce = false;
        var usageChanged = false;

        lock (_stateLock)
        {
            RolloverIfNewDayLocked();

            if (elapsedSeconds <= 0 || !_limits.ContainsKey(exe))
                return;

            limitedExe = exe;
            _usageSeconds.TryGetValue(exe, out var seconds);
            _usageSeconds[exe] = seconds + elapsedSeconds;
            usageChanged = true;

            if (_usageSeconds[exe] >= _limits[exe] * 60.0)
                shouldEnforce = true;
        }

        if (usageChanged)
            UsageChanged?.Invoke();

        if (DateTimeOffset.UtcNow - _lastPersist >= PersistInterval)
            Persist();

        if (shouldEnforce && limitedExe is not null)
            EnforceLimit(limitedExe);
    }

    private void EnforceLimit(string exe)
    {
        string? displayName;
        if (_isHardEnforcement())
        {
            // Hard mode (Sub / Restricted): actually close the app.
            if (!TryKillExe(exe, out displayName))
                return;
        }
        else
        {
            // Soft mode (Trusted Sub): leave it running — the reminder + trust hit raised
            // below is the only consequence.
            displayName = AppDisplayNames.Resolve(exe);
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastLimitNotice < NoticeCooldown)
            return;

        _lastLimitNotice = now;
        int limitMinutes;
        lock (_stateLock)
            limitMinutes = _limits.TryGetValue(exe, out var minutes) ? minutes : 0;

        if (limitMinutes <= 0)
            return;

        LimitReached?.Invoke(displayName!, FormatDuration(TimeSpan.FromMinutes(limitMinutes)));
    }

    private static bool TryKillExe(string exe, out string? displayName)
    {
        displayName = AppDisplayNames.Resolve(exe);
        var processName = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exe[..^4]
            : exe;

        var closedAny = false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                closedAny = true;
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

        return closedAny;
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

    private void RolloverIfNewDayLocked()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _today)
            return;

        _today = today;
        _usageSeconds.Clear();
        _lastLimitNotice = DateTimeOffset.MinValue;
        _lastPersist = DateTimeOffset.MinValue;
        PersistLocked();
    }

    private void PurgeOrphanUsageLocked()
    {
        var toRemove = _usageSeconds.Keys
            .Where(key => !_limits.ContainsKey(key))
            .ToList();

        foreach (var key in toRemove)
            _usageSeconds.Remove(key);

        if (toRemove.Count > 0)
            PersistLocked();
    }

    private void LoadFromStore()
    {
        var stored = _store.Load(_today);
        if (stored.Apps is null)
            return;

        foreach (var pair in stored.Apps)
        {
            if (string.IsNullOrEmpty(pair.Key))
                continue;

            _usageSeconds[pair.Key] = pair.Value;
        }
    }

    private void Persist()
    {
        lock (_stateLock)
            PersistLocked();
    }

    private void PersistLocked()
    {
        _store.Save(new StoredAppTimeUsage
        {
            Date = _today.ToString("yyyy-MM-dd"),
            Apps = new Dictionary<string, double>(_usageSeconds, StringComparer.OrdinalIgnoreCase),
        });

        _lastPersist = DateTimeOffset.UtcNow;
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
