using System.Diagnostics;
using System.Windows.Threading;
using EduGuardAgent.Models;

namespace EduGuardAgent.Services;

internal sealed class StudyDistractionGuard : IDisposable
{
    private static readonly TimeSpan EnforceInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NoticeCooldown = TimeSpan.FromSeconds(15);

    private readonly StudyTimeService _studyTime;
    private readonly UrlBlockingService _urlBlocking;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _sessionBlockedHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastAppNoticeByTarget =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _sitesActive;
    private bool _syncScheduled;

    public StudyDistractionGuard(
        StudyTimeService studyTime,
        UrlBlockingService urlBlocking,
        Dispatcher dispatcher)
    {
        _studyTime = studyTime;
        _urlBlocking = urlBlocking;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(EnforceInterval, DispatcherPriority.Background, OnTick, dispatcher);
        _studyTime.ActiveStateChanged += OnStudyStateChanged;
        _studyTime.SettingsChanged += OnStudyStateChanged;
        ScheduleSync();
    }

    public event Action<string>? DistractingAppBlocked;

    public void Dispose()
    {
        _studyTime.ActiveStateChanged -= OnStudyStateChanged;
        _studyTime.SettingsChanged -= OnStudyStateChanged;
        _timer.Stop();
        RestoreStudyHosts();
    }

    private void OnStudyStateChanged() => ScheduleSync();

    private void ScheduleSync()
    {
        if (_syncScheduled)
            return;

        _syncScheduled = true;
        _dispatcher.BeginInvoke(() =>
        {
            _syncScheduled = false;
            Sync();
        }, DispatcherPriority.Background);
    }

    private void Sync()
    {
        var settings = _studyTime.Settings;
        var shouldBlockSites = _studyTime.IsActiveNow && settings.BlockDistractingSites;
        SyncSites(shouldBlockSites);
    }

    private void SyncSites(bool shouldBlock)
    {
        if (shouldBlock == _sitesActive)
            return;

        if (shouldBlock)
            ApplyStudyHosts();
        else
            RestoreStudyHosts();

        _sitesActive = shouldBlock;
    }

    private void ApplyStudyHosts()
    {
        var permanent = _urlBlocking.BlockedHosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toBlock = StudyDistractionCatalog.SiteHosts
            .Where(host => !permanent.Contains(host))
            .ToList();

        if (toBlock.Count == 0)
            return;

        _urlBlocking.BlockMany(toBlock);
        foreach (var host in toBlock)
        {
            if (_urlBlocking.BlockedHosts.Contains(host))
                _sessionBlockedHosts.Add(host);
        }
    }

    private void RestoreStudyHosts()
    {
        if (_sessionBlockedHosts.Count == 0)
            return;

        _urlBlocking.UnblockMany([.. _sessionBlockedHosts]);
        _sessionBlockedHosts.Clear();
    }

    private volatile bool _enforceScanInFlight;

    private void OnTick(object? sender, EventArgs e)
    {
        var settings = _studyTime.Settings;
        if (!_studyTime.IsActiveNow || !settings.BlockDistractingApps)
            return;

        // Process.GetProcessesByName performs a full system process snapshot internally;
        // calling it once per tracked name (8x/tick) on the UI thread caused visible
        // stutter. Run the scan-and-kill pass on the thread pool instead.
        if (_enforceScanInFlight)
            return;

        _enforceScanInFlight = true;
        Task.Run(() =>
        {
            try
            {
                return TryCloseDistractingApp(out var closedName) ? closedName : null;
            }
            finally
            {
                _enforceScanInFlight = false;
            }
        }).ContinueWith(t =>
        {
            var key = t.Result;
            if (key is null)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                var now = DateTimeOffset.UtcNow;
                if (_lastAppNoticeByTarget.TryGetValue(key, out var last) && now - last < NoticeCooldown)
                    return;

                _lastAppNoticeByTarget[key] = now;
                DistractingAppBlocked?.Invoke(key);
            });
        }, TaskScheduler.Default);
    }

    private static bool TryCloseDistractingApp(out string? closedDisplayName)
    {
        closedDisplayName = null;
        var closedAny = false;
        var targets = StudyDistractionCatalog.ProcessNames;

        // Single full-system enumeration, then filter by name in-memory — avoids one
        // full Process.GetProcessesByName() snapshot per tracked process name.
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var matches = false;
                foreach (var name in targets)
                {
                    if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                    continue;

                process.Kill(entireProcessTree: true);
                closedAny = true;
                closedDisplayName ??= process.ProcessName;
            }
            catch
            {
                // Process may refuse termination, or may no longer be inspectable.
            }
            finally
            {
                process.Dispose();
            }
        }

        return closedAny;
    }
}
