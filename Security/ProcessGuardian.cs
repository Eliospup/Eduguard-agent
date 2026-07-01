using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// Keeps the agent alive even against an elevated Task Manager (which can bypass the
/// process DACL through SeDebugPrivilege).
///
/// A lightweight guard instance of the same executable (launched with <c>--watchdog</c>)
/// relaunches the main agent the instant it is killed, and the main agent relaunches the
/// guard if that one is killed — mutual resurrection.
///
/// Each side is launched through a throwaway <c>--spawn-*</c> instance that exits
/// immediately, so the real process is orphaned (its parent PID points at a dead
/// process). That keeps the guard out of the agent's process tree (and vice versa), so an
/// "End process tree" on one does not take the other down, while both keep the elevated
/// token of their creator.
///
/// A clean, Dom-sanctioned shutdown signals a named event so neither side resurrects the
/// other.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessGuardian
{
    private const string WatchdogArg = "--watchdog";
    private const string SpawnGuardArg = "--spawn-guard";
    private const string SpawnMainArg = "--spawn-main";

    private const string ShutdownEventName = @"Local\EduGuardAgent.IntentionalShutdown";
    private const string GuardMutexName = @"Local\EduGuardAgent.Guardian";
    private const string MainMutexName = @"Local\EduGuardAgent.Main";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir);

    private static readonly string MainPidFile = Path.Combine(DataDir, "agent.pid");
    private static readonly string GuardPidFile = Path.Combine(DataDir, "guard.pid");

    private static readonly TimeSpan GuardPollInterval = TimeSpan.FromMilliseconds(250);
    private static EventWaitHandle? _shutdownEvent;
    private static Mutex? _mainMutexHandle;
    private static Thread? _monitorThread;
    private static volatile bool _shuttingDown;

    public static bool IsGuardInvocation(string[] args) =>
        args.Any(arg => string.Equals(arg, WatchdogArg, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Handles the throwaway launcher roles. Returns true if the current invocation was a
    /// launcher (the caller should exit immediately afterwards).
    /// </summary>
    public static bool TryHandleSpawnRole(string[] args)
    {
        if (args.Any(a => string.Equals(a, SpawnGuardArg, StringComparison.OrdinalIgnoreCase)))
        {
            StartSelf(WatchdogArg);
            return true;
        }

        if (args.Any(a => string.Equals(a, SpawnMainArg, StringComparison.OrdinalIgnoreCase)))
        {
            StartSelf(arguments: null);
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- main role

    public static void StartMainRole()
    {
#if DEBUG
        // Development escape hatch: set EDUGUARD_NO_GUARD=1 to disable resurrection so the
        // agent can be stopped from Task Manager while testing.
        if (string.Equals(Environment.GetEnvironmentVariable("EDUGUARD_NO_GUARD"), "1", StringComparison.Ordinal))
            return;
#endif

        SecurityRuntimeFlags.EnsureLoadedFromDisk();

        // The agent is up: tell the SYSTEM guardian (if installed) to resume guarding.
        SystemGuardian.ClearStandDown();

        try
        {
            _shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
            _mainMutexHandle = new Mutex(initiallyOwned: false, MainMutexName);
            WritePid(MainPidFile);

            _monitorThread = new Thread(MainMonitorLoop)
            {
                IsBackground = true,
                Name = "EduGuardGuardianMonitor",
            };
            _monitorThread.Start();
        }
        catch
        {
            // Self-protection is best-effort; never block startup.
        }
    }

    public static void SignalIntentionalShutdown()
    {
        _shuttingDown = true;

        // A PIN-authorized quit must also stop the SYSTEM guardian from relaunching us.
        SystemGuardian.SignalStandDown();

        try
        {
            _shutdownEvent?.Set();
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ShutdownEventName);
                evt.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                using var evt = new EventWaitHandle(true, EventResetMode.ManualReset, ShutdownEventName);
            }

            _mainMutexHandle?.Dispose();
            _mainMutexHandle = null;
        }
        catch
        {
            // ignored
        }
    }

    private static void MainMonitorLoop()
    {
        while (!_shuttingDown)
        {
            if (SecurityRuntimeFlags.ShouldBlockProcessKillers())
                ProcessKillerDefense.Enforce(Environment.ProcessId);

            if (!IsRealPeerAlive(GuardPidFile))
            {
                SpawnGuard();
                WaitForPeer(GuardPidFile, TimeSpan.FromSeconds(3));
            }

            var guard = Attach(GuardPidFile);
            if (guard is not null)
            {
                try
                {
                    if (guard.WaitForExit((int)GuardPollInterval.TotalMilliseconds))
                    {
                        // Guard exited — loop immediately to respawn.
                    }
                }
                catch { /* re-evaluate */ }
                finally { guard.Dispose(); }
            }
            else
            {
                Thread.Sleep(GuardPollInterval);
            }
        }
    }

    // --------------------------------------------------------------- guard role

    public static void RunGuard()
    {
        SecurityRuntimeFlags.EnsureLoadedFromDisk();

        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, GuardMutexName, out var createdNew);
            if (!createdNew)
                return;
        }
        catch
        {
            return;
        }

        try
        {
            _shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
            WritePid(GuardPidFile);

            while (!IntentionalShutdownRequested())
            {
                if (SecurityRuntimeFlags.ShouldBlockProcessKillers())
                    ProcessKillerDefense.Enforce(Environment.ProcessId);

                if (!IsRealPeerAlive(MainPidFile))
                {
                    if (IntentionalShutdownRequested())
                        return;

                    if (WaitForIntentionalShutdownGrace())
                        return;

                    SpawnMain();
                    WaitForPeer(MainPidFile, TimeSpan.FromSeconds(5));
                    continue;
                }

                var main = Attach(MainPidFile);
                if (main is not null)
                {
                    try
                    {
                        if (main.WaitForExit((int)GuardPollInterval.TotalMilliseconds))
                        {
                            if (WaitForIntentionalShutdownGrace())
                                return;
                        }
                    }
                    catch { /* re-evaluate */ }
                    finally { main.Dispose(); }
                }
                else
                {
                    Thread.Sleep(GuardPollInterval);
                }
            }
        }
        catch
        {
            // ignored — guard is best-effort
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { /* ignored */ }
            mutex.Dispose();
        }
    }

    private static bool IntentionalShutdownRequested()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(ShutdownEventName);
            return evt.WaitOne(0);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// After the main agent exits, wait briefly so its intentional-shutdown signal
    /// can arrive before we resurrect it.
    /// </summary>
    private static bool WaitForIntentionalShutdownGrace()
    {
        for (var i = 0; i < 20; i++)
        {
            if (IntentionalShutdownRequested())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    // ----------------------------------------------------------------- helpers

    private static void SpawnGuard() => StartSelf(SpawnGuardArg);

    private static void SpawnMain() => StartSelf(SpawnMainArg);

    private static Process? StartSelf(string? arguments)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return null;

            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            };

            if (!string.IsNullOrEmpty(arguments))
                startInfo.Arguments = arguments;

            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// True only if the peer's PID file points at a live process that is actually this
    /// same Guardi executable. A named mutex alone is not trusted: an attacker can create
    /// <c>Local\EduGuardAgent.Main</c>/<c>.Guardian</c> themselves, then kill the real
    /// process — the squatted mutex would otherwise make us believe the peer is alive and
    /// suppress resurrection. Verifying a live PID with a matching image path defeats both
    /// mutex squatting and "point the PID file at some unrelated process" tricks.
    /// </summary>
    private static bool IsRealPeerAlive(string pidFile)
    {
        var process = Attach(pidFile);
        if (process is null)
            return false;

        try
        {
            var theirs = TryGetImagePath(process);
            if (theirs is null)
                return true; // Can't read the image (rare) — assume alive to avoid a respawn storm.

            var ours = Environment.ProcessPath;
            return ours is null
                || string.Equals(
                    Path.GetFullPath(theirs),
                    Path.GetFullPath(ours),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // Be conservative: don't respawn on a transient query failure.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string? TryGetImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static void WaitForPeer(string pidFile, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsRealPeerAlive(pidFile) || IntentionalShutdownRequested())
                return;

            Thread.Sleep(150);
        }
    }

    private static Process? Attach(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile))
                return null;

            if (!int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid))
                return null;

            if (pid == Environment.ProcessId)
                return null;

            var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch
        {
            return null;
        }
    }

    private static void WritePid(string pidFile)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(pidFile, Environment.ProcessId.ToString());
        }
        catch
        {
            // ignored
        }
    }
}
