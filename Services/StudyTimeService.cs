using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class StudyTimeService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly StudyTimeSettingsStore _store = new();
    private StudyTimeSettings _settings;
    private bool _wasActive;
    private DateTimeOffset _graceUntil = DateTimeOffset.MinValue;

    public StudyTimeService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _settings = _store.Load();
        _wasActive = IsActiveNow;
        if (_wasActive)
            BeginActivationGrace();

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, OnTick, dispatcher);
        _timer.Start();
    }

    public event Action? SettingsChanged;
    public event Action? ActiveStateChanged;

    public StudyTimeSettings Settings => _settings;

    public bool IsActiveNow => StudyTimeSettings.IsActive(DateTime.Now, _settings);

    /// <summary>True shortly after study time becomes active — blocks still apply, infractions do not.</summary>
    public bool IsInActivationGracePeriod =>
        IsActiveNow && DateTimeOffset.UtcNow < _graceUntil;

    public string StudyTimeLabel
    {
        get
        {
            if (IsActiveNow)
                return BuildActiveLabel(DateTime.Now);

            return _settings.DisplayLabel;
        }
    }

    public string ActiveUntilLabel =>
        IsActiveNow ? _settings.ActiveUntilLabel(DateTime.Now) : string.Empty;

    public void Update(StudyTimeSettings settings)
    {
        var scheduleChanged = settings.ScheduleKey != _settings.ScheduleKey;
        _settings = settings;
        _store.Save(settings);

        var previouslyActive = _wasActive;
        var nowActive = IsActiveNow;
        ApplyActiveTransition(previouslyActive, nowActive);

        if (!scheduleChanged && nowActive == previouslyActive)
            return;

        RaiseNotifications(scheduleChanged, nowActive != previouslyActive);
    }

    public void Dispose() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var previouslyActive = _wasActive;
        var nowActive = IsActiveNow;
        if (nowActive == previouslyActive)
        {
            if (_settings.HasSchedule)
                RaiseNotifications(scheduleChanged: false, activeChanged: false, refreshLabel: true);

            return;
        }

        ApplyActiveTransition(previouslyActive, nowActive);
        RaiseNotifications(scheduleChanged: false, activeChanged: true);
    }

    private void ApplyActiveTransition(bool previouslyActive, bool nowActive)
    {
        _wasActive = nowActive;

        if (nowActive && !previouslyActive)
            BeginActivationGrace();
        else if (!nowActive && previouslyActive)
            _graceUntil = DateTimeOffset.MinValue;
    }

    private void BeginActivationGrace()
    {
        var seconds = Math.Max(1, Config.StudyTimeActivationGraceSeconds);
        _graceUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private void RaiseNotifications(
        bool scheduleChanged,
        bool activeChanged,
        bool refreshLabel = false)
    {
        if (!scheduleChanged && !activeChanged && !refreshLabel)
            return;

        _dispatcher.BeginInvoke(() =>
        {
            if (activeChanged)
                ActiveStateChanged?.Invoke();

            if (scheduleChanged || activeChanged || refreshLabel)
                SettingsChanged?.Invoke();
        }, DispatcherPriority.Background);
    }

    private string BuildActiveLabel(DateTime moment)
    {
        var until = _settings.ActiveUntilLabel(moment);
        var blocks = new List<string>();
        if (_settings.BlockGames)
            blocks.Add("games");
        if (_settings.BlockYoutube)
            blocks.Add("YouTube");
        if (_settings.BlockDistractingSites)
            blocks.Add("social sites");
        if (_settings.BlockDistractingApps)
            blocks.Add("distraction apps");

        var blockLabel = blocks.Count > 0
            ? string.Join(", ", blocks)
            : "distractions";

        return $"Study until {until} — {blockLabel} blocked";
    }
}
