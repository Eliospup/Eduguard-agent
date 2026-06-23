using System.Diagnostics;
using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class YoutubeTimeTracker : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxSampleGap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoticeCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StudyNoticeCooldown = TimeSpan.FromSeconds(15);

    private readonly DispatcherTimer _timer;
    private readonly YoutubeUsageStore _store = new();
    private readonly YoutubeSettingsStore _settingsStore = new();
    private readonly StudyTimeService _studyTime;
    private readonly object _stateLock = new();

    private double _totalSeconds;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private WeeklyMinuteLimits _limits = WeeklyMinuteLimits.Create(
        Config.TestingShortYoutubeTime
            ? 2
            : AgentModeRegistry.Sub.Defaults.YoutubeTimeLimitMinutes);
    private int _limitMinutes;
    private bool _showOverlay = true;
    private bool _restrictedModeEnabled;
    private bool _enforcementEnabled;
    private DateTimeOffset _lastLimitNotice = DateTimeOffset.MinValue;
    private readonly Dictionary<string, DateTimeOffset> _lastStudyNoticeByTarget =
        new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastPersist = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.MinValue;
    private bool _wasActive;

    public YoutubeTimeTracker(Dispatcher dispatcher, StudyTimeService studyTime)
    {
        _studyTime = studyTime;
        _timer = new DispatcherTimer(SampleInterval, DispatcherPriority.Normal, OnTick, dispatcher);
        LoadFromStore();
        LoadSettingsFromStorage();
        RefreshTodayLimit();
    }

    public event Action? UsageChanged;
    public event Action<string>? LimitReached;
    public event Action<string>? StudyModeBlocked;
    public event Action<YoutubeHudState?>? HudStateChanged;

    public int LimitMinutes => _limitMinutes;

    public bool ShowOverlay => _showOverlay;

    public TimeSpan TotalUsage
    {
        get
        {
            lock (_stateLock)
                return TimeSpan.FromSeconds(_totalSeconds);
        }
    }

    public TimeSpan LimitDuration => TimeSpan.FromMinutes(_limitMinutes);

    public double LimitSeconds => _limitMinutes * 60.0;

    public bool IsOverLimit
    {
        get
        {
            lock (_stateLock)
                return _totalSeconds >= LimitSeconds;
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
                    : Math.Min(_totalSeconds / LimitSeconds * 100.0, 100.0);
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
        _wasActive = false;
        Persist();
        HudStateChanged?.Invoke(null);
    }

    public void SetLimitMinutes(int minutes) =>
        ApplyLimits(WeeklyMinuteLimits.Parse(minutes, null, _limits, _limits.DefaultMinutes));

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

    public void SetShowOverlay(bool show)
    {
        if (_showOverlay == show)
            return;

        _showOverlay = show;
        PersistSettings();

        if (!show)
            HudStateChanged?.Invoke(null);
    }

    public bool RestrictedModeEnabled => _restrictedModeEnabled;

    public bool ShouldBlockYoutubeAccess =>
        _enforcementEnabled && (
            IsOverLimit
            || (_studyTime.IsActiveNow && _studyTime.Settings.BlockYoutube));

    public string YoutubeBlockReason
    {
        get
        {
            if (!_enforcementEnabled)
                return string.Empty;

            if (IsOverLimit)
                return "limit";

            if (_studyTime.IsActiveNow && _studyTime.Settings.BlockYoutube)
                return "study";

            return string.Empty;
        }
    }

    public void SetRestrictedModeEnabled(bool enabled)
    {
        if (_restrictedModeEnabled == enabled)
            return;

        _restrictedModeEnabled = enabled;
        PersistSettings();
    }

    public void ApplySettings(YoutubeSettingsPayload settings)
    {
        if (settings.DailyLimitMinutes is not null || settings.WeeklyLimits is not null)
        {
            ApplyLimits(WeeklyMinuteLimits.Parse(
                settings.DailyLimitMinutes,
                settings.WeeklyLimits,
                _limits,
                _limits.DefaultMinutes));
        }

        if (settings.ShowOverlay is { } show)
            SetShowOverlay(show);

        if (settings.RestrictedModeEnabled is { } restricted)
            SetRestrictedModeEnabled(restricted);
    }

    public void ApplyUsageTo(HeartbeatRequest request)
    {
        request.YoutubeUsage = BuildHeartbeatUsage();
    }

    public YoutubeUsagePayload BuildHeartbeatUsage()
    {
        lock (_stateLock)
        {
            return new YoutubeUsagePayload
            {
                Date = _today.ToString("yyyy-MM-dd"),
                TotalSeconds = (int)Math.Round(_totalSeconds),
                LimitMinutes = _limitMinutes,
            };
        }
    }

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

        DetectedYoutubeSession? activeSession = null;
        var shouldPersist = false;
        var shouldEnforceLimit = false;

        if (_enforcementEnabled && _studyTime.IsActiveNow && _studyTime.Settings.BlockYoutube)
        {
            if (YoutubeForegroundDetector.TryGetActiveSession(out var studySession))
                EnforceStudyMode(studySession);

            RaiseHudState(null);
            UsageChanged?.Invoke();
            return;
        }

        if (_enforcementEnabled
            && YoutubeForegroundDetector.TryGetActiveSession(out var session)
            && elapsedSeconds > 0)
        {
            activeSession = session;

            lock (_stateLock)
            {
                RolloverIfNewDayLocked();
                _totalSeconds += elapsedSeconds;
                shouldPersist = DateTimeOffset.UtcNow - _lastPersist >= PersistInterval;
                shouldEnforceLimit = _enforcementEnabled && _totalSeconds >= LimitSeconds;
            }
        }

        if (_wasActive && activeSession is null)
            shouldPersist = true;

        _wasActive = activeSession is not null;

        if (shouldPersist)
            Persist();

        if (shouldEnforceLimit && activeSession is { } enforced)
            EnforceLimit(enforced);

        RaiseHudState(activeSession);
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

    private void RaiseHudState(DetectedYoutubeSession? session)
    {
        if (!_showOverlay
            || !_enforcementEnabled
            || session is not { } active)
        {
            HudStateChanged?.Invoke(null);
            return;
        }

        YoutubeHudState hudState;
        lock (_stateLock)
        {
            var remaining = LimitDuration - TimeSpan.FromSeconds(_totalSeconds);
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var progress = LimitSeconds <= 0
                ? 0
                : Math.Min(_totalSeconds / LimitSeconds * 100.0, 100.0);

            hudState = new YoutubeHudState
            {
                SourceLabel = active.SourceLabel,
                RemainingLabel = FormatCountdown(remaining),
                Progress = progress,
            };
        }

        HudStateChanged?.Invoke(hudState);
    }

    private void EnforceLimit(DetectedYoutubeSession session)
    {
        if (!YoutubeSessionBlocker.TryBlock(session, "limit"))
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - _lastLimitNotice < NoticeCooldown)
            return;

        _lastLimitNotice = now;
        LimitReached?.Invoke(FormatDuration(LimitDuration));
    }

    private void EnforceStudyMode(DetectedYoutubeSession session)
    {
        if (!YoutubeSessionBlocker.TryBlock(session, "study"))
            return;

        var key = session.SourceLabel;
        var now = DateTimeOffset.UtcNow;
        if (_lastStudyNoticeByTarget.TryGetValue(key, out var last) && now - last < StudyNoticeCooldown)
            return;

        _lastStudyNoticeByTarget[key] = now;
        StudyModeBlocked?.Invoke(key);
    }

    private void RolloverIfNewDayLocked()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _today)
            return;

        _today = today;
        _totalSeconds = 0;
        _lastLimitNotice = DateTimeOffset.MinValue;
        _lastPersist = DateTimeOffset.MinValue;
        RefreshTodayLimit();
        PersistLocked();
    }

    private void LoadFromStore()
    {
        var stored = _store.Load(_today);
        _totalSeconds = stored.TotalSeconds;
    }

    private void LoadSettingsFromStorage()
    {
        var settings = _settingsStore.Load();
        _limits = WeeklyMinuteLimits.Parse(
            settings.DailyLimitMinutes,
            settings.WeeklyLimits,
            null,
            Config.TestingShortYoutubeTime
                ? 2
                : AgentModeRegistry.Sub.Defaults.YoutubeTimeLimitMinutes);

        if (settings.ShowOverlay is { } show)
            _showOverlay = show;

        if (settings.RestrictedModeEnabled is { } restricted)
            _restrictedModeEnabled = restricted;
    }

    private void PersistSettings()
    {
        _settingsStore.Save(new StoredYoutubeSettings
        {
            DailyLimitMinutes = _limits.DefaultMinutes,
            WeeklyLimits = _limits.HasOverrides
                ? WeeklyMinuteLimits.SerializeDays(_limits.DayMinutes)
                : null,
            ShowOverlay = _showOverlay,
            RestrictedModeEnabled = _restrictedModeEnabled,
        });
    }

    private void Persist()
    {
        lock (_stateLock)
            PersistLocked();
    }

    private void PersistLocked()
    {
        _store.Save(new StoredYoutubeUsage
        {
            Date = _today.ToString("yyyy-MM-dd"),
            TotalSeconds = _totalSeconds,
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
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes))}m";
    }
}
