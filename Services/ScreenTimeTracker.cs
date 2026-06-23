using System.Windows.Threading;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class ScreenTimeTracker : IDisposable
{
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _timer;
    private readonly ScreenTimeStore _store = new();
    private DateTime? _sessionStart;
    private double _accumulatedSeconds;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private DateTimeOffset _lastPersist = DateTimeOffset.MinValue;

    public ScreenTimeTracker(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, dispatcher);
        LoadFromStore();
    }

    public event Action? ElapsedChanged;

    public TimeSpan Elapsed
    {
        get
        {
            var sessionSeconds = _sessionStart is null
                ? 0
                : (DateTime.UtcNow - _sessionStart.Value).TotalSeconds;
            return TimeSpan.FromSeconds(_accumulatedSeconds + sessionSeconds);
        }
    }

    public void Start()
    {
        RolloverIfNewDay();
        _sessionStart ??= DateTime.UtcNow;
        _timer.Start();
        ElapsedChanged?.Invoke();
    }

    public void Stop()
    {
        FlushSession();
        _timer.Stop();
        Persist();
        ElapsedChanged?.Invoke();
    }

    public void Dispose()
    {
        FlushSession();
        Persist();
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        RolloverIfNewDay();
        ElapsedChanged?.Invoke();

        if (DateTimeOffset.UtcNow - _lastPersist >= PersistInterval)
            Persist();
    }

    private void FlushSession()
    {
        if (_sessionStart is null)
            return;

        _accumulatedSeconds += (DateTime.UtcNow - _sessionStart.Value).TotalSeconds;
        _sessionStart = null;
    }

    private void RolloverIfNewDay()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _today)
            return;

        FlushSession();
        _today = today;
        _accumulatedSeconds = 0;
        _lastPersist = DateTimeOffset.MinValue;
        Persist();
    }

    private void LoadFromStore()
    {
        var stored = _store.Load(_today);
        _accumulatedSeconds = stored.TotalSeconds;
    }

    private void Persist()
    {
        var sessionSeconds = _sessionStart is null
            ? 0
            : (DateTime.UtcNow - _sessionStart.Value).TotalSeconds;

        _store.Save(new StoredScreenTimeUsage
        {
            Date = _today.ToString("yyyy-MM-dd"),
            TotalSeconds = _accumulatedSeconds + sessionSeconds,
        });

        _lastPersist = DateTimeOffset.UtcNow;
    }
}
