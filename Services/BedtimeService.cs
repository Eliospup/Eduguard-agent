using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class BedtimeService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly BedtimeSettingsStore _store = new();
    private BedtimeSettings _settings;
    private BedtimeDailyState _dailyState;

    public BedtimeService(Dispatcher dispatcher)
    {
        _settings = _store.Load();
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        _dailyState = _store.LoadDailyState(today, _settings.TodayScheduleKey(now));
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, OnTick, dispatcher);
        _timer.Start();
        SyncBlueLightFilter(now);
    }

    public event Action<BedtimeWarningKind>? WarningDue;
    public event Action? BedtimeReached;
    public event Action? WakeTimeReached;
    public event Action? SettingsChanged;
    public event Action<bool, BlueLightFilterPhase>? BlueLightFilterActiveChanged;

    public BedtimeSettings Settings => _settings;

    public bool IsBlueLightFilterActive { get; private set; }

    public BlueLightFilterPhase BlueLightFilterPhase { get; private set; } = BlueLightFilterPhase.Off;

    public string BedtimeLabel => _settings.DisplayLabel;

    public string BedtimeCardLabel => _settings.CardDisplayLabel;

    public void Update(BedtimeSettings settings)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var oldTodayKey = _settings.TodayScheduleKey(now);

        _settings = settings;
        _store.Save(settings);

        if (oldTodayKey != settings.TodayScheduleKey(now))
            ResetDailyStateFor(today);
        else
            SyncDailyStateKey(now);

        SettingsChanged?.Invoke();
        SyncLockState();
        SyncBlueLightFilter(now);
    }

    public void SyncLockState()
    {
        if (_settings.IsInLockWindow(DateTime.Now))
        {
            if (!_dailyState.BedtimeTriggered)
            {
                _dailyState = _dailyState.WithBedtimeTriggered();
                PersistDailyState();
            }

            BedtimeReached?.Invoke();
            return;
        }

        if (_dailyState.BedtimeTriggered)
            TriggerWake();
    }

    public void Dispose() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        EnsureDailyStateCurrent(now, today);

        if (_settings.IsInLockWindow(now))
        {
            HandleLockWindow(now, today);
            SyncBlueLightFilter(now);
            return;
        }

        if (_dailyState.BedtimeTriggered)
            TriggerWake();

        var (enabled, _, _) = _settings.Resolve(now);
        if (enabled)
            HandleBedtimeWarnings(now, today);

        SyncBlueLightFilter(now);
    }

    private void EnsureDailyStateCurrent(DateTime now, DateOnly today)
    {
        var todayKey = _settings.TodayScheduleKey(now);
        if (_dailyState.Date != today || _dailyState.ScheduleKey != todayKey)
            ResetDailyStateFor(today);
    }

    private void SyncDailyStateKey(DateTime now)
    {
        var todayKey = _settings.TodayScheduleKey(now);
        if (_dailyState.ScheduleKey == todayKey)
            return;

        _dailyState = _dailyState with { ScheduleKey = todayKey };
        PersistDailyState();
    }

    private void HandleLockWindow(DateTime now, DateOnly today)
    {
        if (!_dailyState.BedtimeTriggered)
        {
            _dailyState = _dailyState.WithBedtimeTriggered();
            PersistDailyState();
            BedtimeReached?.Invoke();
        }
    }

    private void HandleBedtimeWarnings(DateTime now, DateOnly today)
    {
        var (enabled, bedtime, _) = _settings.Resolve(now);
        if (!enabled)
            return;

        var bedtimeAt = today.ToDateTime(bedtime);
        if (now >= bedtimeAt)
            return;

        var remaining = bedtimeAt - now;
        if (remaining <= TimeSpan.FromMinutes(5))
            TryWarn(BedtimeWarningKind.FiveMinutes);
        else if (remaining <= TimeSpan.FromMinutes(30))
            TryWarn(BedtimeWarningKind.ThirtyMinutes);
        else if (remaining <= TimeSpan.FromHours(1))
            TryWarn(BedtimeWarningKind.OneHour);
    }

    private void TriggerWake()
    {
        ResetDailyStateFor(DateOnly.FromDateTime(DateTime.Now));
        WakeTimeReached?.Invoke();
    }

    private void TryWarn(BedtimeWarningKind kind)
    {
        if (HasWarned(kind))
            return;

        _dailyState = _dailyState.WithWarning(kind);
        PersistDailyState();
        WarningDue?.Invoke(kind);
    }

    private bool HasWarned(BedtimeWarningKind kind) => kind switch
    {
        BedtimeWarningKind.OneHour => _dailyState.WarnedOneHour,
        BedtimeWarningKind.ThirtyMinutes => _dailyState.WarnedThirtyMinutes,
        BedtimeWarningKind.FiveMinutes => _dailyState.WarnedFiveMinutes,
        _ => true,
    };

    private void ResetDailyStateFor(DateOnly day)
    {
        var moment = day == DateOnly.FromDateTime(DateTime.Now)
            ? DateTime.Now
            : day.ToDateTime(TimeOnly.MinValue);
        _dailyState = BedtimeDailyState.Empty(day, _settings.TodayScheduleKey(moment));
        _store.ClearDailyState();
        SyncBlueLightFilter(moment);
    }

    private void SyncBlueLightFilter(DateTime now)
    {
        var (active, phase) = ResolveBlueLightFilterState(now);
        if (active == IsBlueLightFilterActive && (!active || phase == BlueLightFilterPhase))
            return;

        IsBlueLightFilterActive = active;
        BlueLightFilterPhase = phase;
        BlueLightFilterActiveChanged?.Invoke(active, phase);
    }

    private (bool Active, BlueLightFilterPhase Phase) ResolveBlueLightFilterState(DateTime now)
    {
        var (enabled, bedtime, _) = _settings.Resolve(now);
        if (!enabled || !_settings.BlueLightFilterEnabled)
            return (false, BlueLightFilterPhase.Off);

        if (_settings.IsInLockWindow(now))
            return (true, BlueLightFilterPhase.Lock);

        var today = DateOnly.FromDateTime(now);
        var bedtimeAt = today.ToDateTime(bedtime);
        if (now >= bedtimeAt)
            return (false, BlueLightFilterPhase.Off);

        var remaining = bedtimeAt - now;
        if (remaining <= TimeSpan.FromMinutes(30))
            return (true, BlueLightFilterPhase.Late);

        if (remaining <= TimeSpan.FromHours(1))
            return (true, BlueLightFilterPhase.Early);

        return (false, BlueLightFilterPhase.Off);
    }

    private void PersistDailyState() =>
        _store.SaveDailyState(_dailyState);
}
