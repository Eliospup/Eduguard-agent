using System.Diagnostics;
using System.Runtime.Versioning;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Enforces the configurable per-mode security locks (Task Manager, Registry Editor,
/// command interpreters, system configuration tools, process explorers, ...).
///
/// Detection is two-layered: process name AND PE OriginalFilename metadata.  A
/// supervised user who renames cmd.exe to homework.exe is still caught because the
/// PE version-info field preserves the original identity.  The full process list is
/// enumerated once per tick and each process is checked against the blocked set by
/// both its running name and its PE original name.
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
        "resmon.exe",
        "eventvwr.exe",
        "autoruns.exe", "autoruns64.exe",
    ];

    private static readonly string[] ControlPanelExes =
    [
        "control.exe",
    ];

    private static readonly string[] SystemConfigExes =
    [
        "msconfig.exe",
        "mmc.exe",
    ];

    private static readonly string[] CommandPromptExes =
    [
        "cmd.exe",
        "reg.exe",
        "net.exe",
        "net1.exe",
        "sc.exe",
        "schtasks.exe",
        "bcdedit.exe",
        "netsh.exe",
        "wmic.exe",
        "cscript.exe",
        "wscript.exe",
        "mshta.exe",
    ];

    // Tools Windows legitimately spawns in the user session (Task Scheduler,
    // network stack, WMI providers, login scripts…). Kill them silently —
    // no trust penalty — because the user almost certainly didn't launch them.
    private static readonly HashSet<string> SilentKillExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "schtasks.exe",
        "netsh.exe",
        "net.exe",
        "net1.exe",
        "sc.exe",
        "wmic.exe",
        "cscript.exe",
        "wscript.exe",
    };

    private static readonly string[] PowerShellExes =
    [
        "powershell.exe",
        "pwsh.exe",
        "powershell_ise.exe",
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
    private volatile bool _enforceInFlight;

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
            {
                foreach (var exe in CommandPromptExes)
                    _killProcesses.Add(exe);
            }

            if (features.BlockPowerShell)
            {
                foreach (var exe in PowerShellExes)
                    _killProcesses.Add(exe);
            }

            if (features.BlockSystemConfig)
            {
                foreach (var exe in SystemConfigExes)
                    _killProcesses.Add(exe);
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

    /// <summary>
    /// Enumerates all running processes once and checks each against the blocked set
    /// by both process name and PE OriginalFilename.  This catches renamed executables.
    /// </summary>
    private void EnforceNow()
    {
        if (_enforceInFlight)
            return;

        _enforceInFlight = true;
        try
        {
            PruneReportedPids();

            HashSet<string> killTargets;
            lock (_lock)
            {
                if (_disposed)
                    return;

                if (_killProcesses.Count == 0)
                    return;

                killTargets = new HashSet<string>(_killProcesses, StringComparer.OrdinalIgnoreCase);
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return;
            }

            var alivePids = new HashSet<int>(processes.Length);

            foreach (var process in processes)
            {
                try
                {
                    var pid = process.Id;
                    alivePids.Add(pid);

                    if (pid == Environment.ProcessId)
                        continue;

                    // Session 0 = SYSTEM/services — never user-initiated.
                    // Windows legitimately spawns schtasks.exe, reg.exe, etc.
                    if (process.SessionId == 0)
                        continue;

                    var processExe = ProcessNameToExe(process);
                    if (processExe is null)
                        continue;

                    // Layer 1: direct process name match (fast path).
                    if (killTargets.Contains(processExe))
                    {
                        KillAndReport(process, processExe);
                        continue;
                    }

                    // Layer 2: PE OriginalFilename — catches renamed executables.
                    var originalExe = ProcessIdentifier.GetOriginalFilename(process);
                    if (originalExe is not null && killTargets.Contains(originalExe))
                    {
                        KillAndReport(process, originalExe);
                    }
                }
                catch
                {
                    // Protected/system processes may throw — skip silently.
                }
                finally
                {
                    process.Dispose();
                }
            }

            ProcessIdentifier.PruneCache(alivePids);
        }
        finally
        {
            _enforceInFlight = false;
        }
    }

    private void KillAndReport(Process process, string matchedExe)
    {
        var pid = process.Id;
        var report = false;
        lock (_lock)
            report = _reportedPids.Add(pid);

        if (report && !SilentKillExes.Contains(matchedExe))
            ToolBlocked?.Invoke(matchedExe);

        try
        {
            process.Kill(entireProcessTree: true);
            _log?.Invoke($"Security lock closed {matchedExe} (PID {pid}).");
        }
        catch
        {
            // Elevated tools may refuse termination — the bypass was still reported.
        }
    }

    private static string? ProcessNameToExe(Process process)
    {
        try
        {
            var name = process.ProcessName;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"{name}.exe";
        }
        catch
        {
            return null;
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
