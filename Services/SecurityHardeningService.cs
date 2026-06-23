using System.Diagnostics;
using System.Runtime.Versioning;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Enforces the configurable per-mode security locks (Task Manager, Registry Editor,
/// command interpreters, system configuration tools, process explorers, …).
///
/// Enforcement is process-based: any blocked tool is terminated on a fast tick so it
/// works regardless of which user hive the elevated agent runs under. This is the
/// reliable layer; it does not rely on per-user registry policies which would land in
/// the wrong hive when an over-the-shoulder admin elevates the agent.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SecurityHardeningService : IDisposable
{
    private static readonly TimeSpan EnforceInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan KillerEnforceInterval = TimeSpan.FromMilliseconds(400);

    private static readonly string[] ProcessToolExes =
    [
        "procexp.exe", "procexp64.exe",
        "processhacker.exe",
        "systeminformer.exe",
        "procmon.exe", "procmon64.exe",
        "pchunter.exe", "pchunter64.exe",
        "perfmon.exe",
    ];

    private static readonly string[] ControlPanelExes =
    [
        "control.exe",
        "systemsettings.exe",
    ];

    private readonly object _lock = new();
    private readonly HashSet<string> _killProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _reportedPids = new();
    private readonly HashSet<int> _reportedKillerPids = new();
    private readonly Action<string>? _log;
    private Timer? _timer;
    private Timer? _killerTimer;
    private bool _blockProcessKillers = true;
    private bool _disposed;
    private volatile bool _killerEnforceInFlight;

    public SecurityHardeningService(Action<string>? log = null) => _log = log;

    /// <summary>Raised with the executable name whenever a hardened tool is force-closed.</summary>
    public event Action<string>? ToolBlocked;

    public IReadOnlyCollection<string> BlockedProcesses
    {
        get
        {
            lock (_lock)
                return _killProcesses.ToArray();
        }
    }

    public void Apply(ModeFeatures features)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _killProcesses.Clear();

            if (features.BlockTaskManager)
                _killProcesses.Add("taskmgr.exe");

            if (features.BlockRegistryEditor)
                _killProcesses.Add("regedit.exe");

            if (features.BlockCommandPrompt)
                _killProcesses.Add("cmd.exe");

            if (features.BlockPowerShell)
            {
                _killProcesses.Add("powershell.exe");
                _killProcesses.Add("pwsh.exe");
                _killProcesses.Add("powershell_ise.exe");
            }

            if (features.BlockSystemConfig)
            {
                _killProcesses.Add("msconfig.exe");
                _killProcesses.Add("mmc.exe");
            }

            if (features.BlockControlPanel)
            {
                foreach (var exe in ControlPanelExes)
                    _killProcesses.Add(exe);
            }

            if (features.BlockProcessTools)
            {
                foreach (var exe in ProcessToolExes)
                    _killProcesses.Add(exe);
            }

            _blockProcessKillers = features.BlockProcessKillers;
            SecurityRuntimeFlags.Persist(features);

            _timer ??= new Timer(_ => EnforceNow(), null, TimeSpan.Zero, EnforceInterval);

            if (features.BlockProcessKillers)
            {
                _killerTimer ??= new Timer(_ => EnforceKillersNow(), null, TimeSpan.Zero, KillerEnforceInterval);
            }
            else if (_killerTimer is not null)
            {
                _killerTimer.Dispose();
                _killerTimer = null;
                _reportedKillerPids.Clear();
            }
        }

        EnforceNow();
        if (features.BlockProcessKillers)
            EnforceKillersNow();
    }

    private void EnforceKillersNow()
    {
        if (!_blockProcessKillers || !SecurityRuntimeFlags.ShouldBlockProcessKillers())
            return;

        if (_killerEnforceInFlight)
            return;

        _killerEnforceInFlight = true;
        try
        {
            PruneReportedKillerPids();

            BlockedProcessKiller[] blocked;
            lock (_lock)
            {
                if (_disposed)
                    return;
            }

            blocked = ProcessKillerDefense.Enforce(Environment.ProcessId).ToArray();
            foreach (var killer in blocked)
            {
                var report = false;
                lock (_lock)
                    report = _reportedKillerPids.Add(killer.ProcessId);

                if (!report)
                    continue;

                ToolBlocked?.Invoke(killer.Label);
                _log?.Invoke($"Process-killer defense closed {killer.Label}.");
            }
        }
        finally
        {
            _killerEnforceInFlight = false;
        }
    }

    private void PruneReportedKillerPids()
    {
        int[] snapshot;
        lock (_lock)
            snapshot = _reportedKillerPids.ToArray();

        foreach (var pid in snapshot)
        {
            if (IsProcessAlive(pid))
                continue;

            lock (_lock)
                _reportedKillerPids.Remove(pid);
        }
    }

    private void EnforceNow()
    {
        PruneReportedPids();

        string[] killTargets;
        lock (_lock)
        {
            if (_disposed)
                return;

            killTargets = _killProcesses.ToArray();
        }

        foreach (var exe in killTargets)
            EnforceProcess(exe);
    }

    private void EnforceProcess(string exe)
    {
        var processName = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exe[..^4]
            : exe;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                    continue;

                var pid = process.Id;
                var report = false;
                lock (_lock)
                    report = _reportedPids.Add(pid);

                if (report)
                    ToolBlocked?.Invoke(exe);

                process.Kill(entireProcessTree: true);
                _log?.Invoke($"Security lock closed {exe}.");
            }
            catch
            {
                // Elevated tools may refuse termination — the bypass was still reported.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void PruneReportedPids()
    {
        int[] snapshot;
        lock (_lock)
            snapshot = _reportedPids.ToArray();

        foreach (var pid in snapshot)
        {
            if (IsProcessAlive(pid))
                continue;

            lock (_lock)
                _reportedPids.Remove(pid);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _killerTimer?.Dispose();
            _killerTimer = null;
            _killProcesses.Clear();
            _reportedPids.Clear();
            _reportedKillerPids.Clear();
        }
    }
}
