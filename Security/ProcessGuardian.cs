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
    private static readonly string ForceKillMarker = Path.Combine(DataDir, "force_kill.marker");
    private static readonly string RunningMarker = Path.Combine(DataDir, "running.marker");

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

    /// <summary>
    /// Claims the single-instance slot for the interactive agent and, if this process won it,
    /// starts the mutual-resurrection monitor. Returns <c>false</c> when another main agent is
    /// already running (a manual relaunch, or a resurrection racing a still-alive instance
    /// during a slow restart) — the caller must then exit WITHOUT opening a second window or
    /// starting a second supervision stack, so two Guardi apps can never run at once.
    /// </summary>
    public static bool TryStartMainRole()
    {
#if DEBUG
        // Development escape hatch: set EDUGUARD_NO_GUARD=1 to disable resurrection so the
        // agent can be stopped from Task Manager while testing. Still single-instance below.
        var noGuard = string.Equals(Environment.GetEnvironmentVariable("EDUGUARD_NO_GUARD"), "1", StringComparison.Ordinal);
#endif

        // Single-instance gate: the first main agent to create the named mutex owns supervision.
        // A second instance sees createdNew == false and bows out. The mutex object lives only as
        // long as a handle is open, so when the owner exits the slot frees for a legit relaunch.
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MainMutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                AuditLog.Write("Another Guardi agent is already running — this instance is exiting to stay single-instance.");
                return false;
            }

            _mainMutexHandle = mutex;
        }
        catch
        {
            // If the mutex can't be created we can't prove uniqueness; proceed rather than
            // leave the machine unsupervised (worst case matches the old always-start behavior).
        }

        SecurityRuntimeFlags.EnsureLoadedFromDisk();

        // The agent is up: tell the SYSTEM guardian (if installed) to resume guarding.
        SystemGuardian.ClearStandDown();

#if DEBUG
        if (noGuard)
            return true;
#endif

        try
        {
            _shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName);
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

        return true;
    }

    public static void SignalIntentionalShutdown()
    {
        _shuttingDown = true;
        ClearRunningMarker();

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

        // Actively terminate the guard process so it doesn't linger in Task Manager.
        KillGuardProcess();

        // Stop the Boot Guardian service (it stays installed for next boot).
        BootGuardianService.Stop();
    }

    /// <summary>
    /// Called once at startup. If the running marker exists, the previous instance
    /// was force-killed (a clean shutdown deletes it). Returns true once, then
    /// writes a fresh marker for this session.
    /// </summary>
    public static bool ConsumeForceKillMarker()
    {
        try
        {
            var wasForceKilled = File.Exists(RunningMarker);

            // Also check the guard-written marker (belt and suspenders).
            if (!wasForceKilled && File.Exists(ForceKillMarker))
                wasForceKilled = true;

            try { File.Delete(ForceKillMarker); } catch { }

            // Write the running marker for this session.
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(RunningMarker, DateTimeOffset.UtcNow.ToString("o"));

            return wasForceKilled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Deletes the running marker on clean shutdown so the next start knows it was clean.</summary>
    public static void ClearRunningMarker()
    {
        try { File.Delete(RunningMarker); } catch { }
    }

    private static void WriteForceKillMarker()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ForceKillMarker, DateTimeOffset.UtcNow.ToString("o"));
            AuditLog.Write("SECURITY: Agent was force-killed — marker written for trust penalty.");
        }
        catch { /* best-effort */ }
    }

    private static void KillGuardProcess()
    {
        try
        {
            var process = Attach(GuardPidFile);
            if (process is null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { /* best-effort */ }
            finally { process.Dispose(); }
        }
        catch { /* ignored */ }
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

                    WriteForceKillMarker();
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
