using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// A real Windows Service (LocalSystem) whose sole purpose is Safe Mode resilience. The
/// scheduled-task guardian (<see cref="SystemGuardian"/>) does NOT run in Safe Mode — Task
/// Scheduler is disabled there — so booting into Safe Mode is otherwise a clean bypass window.
/// This service is registered under the SafeBoot\Minimal and SafeBoot\Network keys, so the SCM
/// starts it even in Safe Mode, where it re-asserts the protected-state ACL and (in Safe Mode)
/// resurrects the interactive agent so supervision comes back up.
///
/// Deliberately additive and low-risk:
///  - Start type is AUTO with error control NORMAL (never boot-critical): if the service fails
///    to start, Windows logs it and boots anyway.
///  - In NORMAL boot it only re-asserts the ACL (the scheduled-task guardian owns resurrection
///    and the IPC pipe), so it never fights the proven normal-mode path.
///  - Fully reversible: <see cref="TryUninstall"/> stops + deletes the service and removes the
///    SafeBoot keys; also wired into <see cref="SecurityTeardown"/> / <c>--uninstall</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BootGuardianService
{
    public const string ServiceArg = "--service";
    public const string ServiceName = "GuardiBoot";
    private const string DisplayName = "Guardi Boot Guardian";
    private const string StandDownMarker = "guardian.standdown";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // ---- SCM lifecycle state (service process only) ----------------------------------------
    private static ServiceMainProc? _serviceMainDelegate;
    private static HandlerProc? _handlerDelegate;
    private static IntPtr _statusHandle;
    private static volatile bool _stopRequested;
    private static uint _checkPoint;

    // ==========================================================================================
    // Install / uninstall (called from the elevated agent, not the service process)
    // ==========================================================================================

    public static bool IsInstalled()
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
            return false;
        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero)
                return false;
            CloseServiceHandle(svc);
            return true;
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool TryInstall(out string? error)
    {
        error = null;
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            error = "Could not resolve the Guardi executable path.";
            return false;
        }

        var scm = OpenSCManager(null, null, SC_MANAGER_CREATE_SERVICE);
        if (scm == IntPtr.Zero)
        {
            error = $"OpenSCManager failed (Win32 {Marshal.GetLastWin32Error()}).";
            return false;
        }

        try
        {
            // Exact quoting under our control (no sc.exe binPath parsing quirks).
            var binPath = $"\"{exePath}\" {ServiceArg}";
            var svc = CreateService(
                scm,
                ServiceName,
                DisplayName,
                SERVICE_CHANGE_CONFIG,
                SERVICE_WIN32_OWN_PROCESS,
                SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL,   // never boot-critical
                binPath,
                null, IntPtr.Zero, null,
                null,                    // LocalSystem
                null);

            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == ERROR_SERVICE_EXISTS)
                {
                    EnsureSafeBootKeys();
                    error = null;
                    return true;
                }

                error = $"CreateService failed (Win32 {err}).";
                return false;
            }

            CloseServiceHandle(svc);
            EnsureSafeBootKeys();
            AuditLog.Write("Boot guardian service installed (Safe Mode resilience).");
            return true;
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static bool TryUninstall(out string? error)
    {
        error = null;
        // The SYSTEM guardian recreates this service as part of the self-healing mesh, so signal
        // an authorized stand-down first (shared marker) or the removal would be undone.
        SystemGuardian.SignalStandDown();
        RemoveSafeBootKeys();

        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
            return true; // nothing we can do; treat as gone

        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_STOP | DELETE);
            if (svc == IntPtr.Zero)
                return true; // already gone

            try
            {
                var status = default(SERVICE_STATUS);
                ControlService(svc, SERVICE_CONTROL_STOP, ref status); // best-effort stop
                DeleteService(svc);
                AuditLog.Write("Boot guardian service removed.");
                return true;
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    /// <summary>Stops the service without removing it (used on intentional shutdown).</summary>
    public static void Stop()
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
            return;
        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_STOP);
            if (svc == IntPtr.Zero)
                return;
            try
            {
                var status = default(SERVICE_STATUS);
                ControlService(svc, SERVICE_CONTROL_STOP, ref status);
            }
            finally { CloseServiceHandle(svc); }
        }
        catch { /* best-effort */ }
        finally { CloseServiceHandle(scm); }
    }

    /// <summary>Starts the service now (so Safe Mode coverage doesn't wait for a reboot).</summary>
    public static void StartIfNotRunning()
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero)
            return;
        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_START);
            if (svc == IntPtr.Zero)
                return;
            try
            {
                StartService(svc, 0, IntPtr.Zero);
            }
            finally
            {
                CloseServiceHandle(svc);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    // The SafeBoot allow-list: listing the service name here makes the SCM start it when
    // Windows is booted into Safe Mode (Minimal) or Safe Mode with Networking.
    private static readonly string[] SafeBootKeys =
    [
        @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\" + ServiceName,
        @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\" + ServiceName,
    ];

    private static void EnsureSafeBootKeys()
    {
        foreach (var path in SafeBootKeys)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(path);
                key?.SetValue(null, "Service");
            }
            catch (Exception ex)
            {
                AuditLog.Write($"Boot guardian SafeBoot key set failed ({path}): {ex.Message}");
            }
        }
    }

    private static void RemoveSafeBootKeys()
    {
        foreach (var path in SafeBootKeys)
        {
            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            }
            catch
            {
                // best-effort
            }
        }
    }

    // ==========================================================================================
    // Service process entry point (invoked when the exe is launched with --service by the SCM)
    // ==========================================================================================

    public static void RunAsService()
    {
        _serviceMainDelegate = ServiceMain;
        var table = new[]
        {
            new SERVICE_TABLE_ENTRY { lpServiceName = ServiceName, lpServiceProc = Marshal.GetFunctionPointerForDelegate(_serviceMainDelegate) },
            new SERVICE_TABLE_ENTRY { lpServiceName = null, lpServiceProc = IntPtr.Zero },
        };

        // Blocks until the service stops. Fails only if the process wasn't actually started by
        // the SCM (e.g. run by hand) — harmless, we just exit.
        if (!StartServiceCtrlDispatcher(table))
            AuditLog.Write($"Boot guardian dispatcher not started (Win32 {Marshal.GetLastWin32Error()}).");
    }

    private static void ServiceMain(uint argc, IntPtr argv)
    {
        _handlerDelegate = ServiceControlHandler;
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, _handlerDelegate, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero)
            return;

        ReportStatus(SERVICE_START_PENDING, waitHintMs: 3000);
        ReportStatus(SERVICE_RUNNING, waitHintMs: 0);

        try
        {
            GuardianLoop();
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Boot guardian loop crashed: {ex.Message}");
        }

        ReportStatus(SERVICE_STOPPED, waitHintMs: 0);
    }

    private static uint ServiceControlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case SERVICE_CONTROL_STOP:
            case SERVICE_CONTROL_SHUTDOWN:
                ReportStatus(SERVICE_STOP_PENDING, waitHintMs: 5000);
                _stopRequested = true;
                break;
            case SERVICE_CONTROL_INTERROGATE:
                break;
        }

        return NO_ERROR;
    }

    private static void GuardianLoop()
    {
        var safeMode = IsSafeMode();
        AuditLog.Write($"Boot guardian service running (safeMode={safeMode}).");

        while (!_stopRequested)
        {
            try
            {
                // An authorized teardown signals a valid stand-down before removing us — then we
                // stop self-healing and let removal proceed.
                if (StandDownRequested())
                {
                    // nothing to do; wait for SCM stop / removal
                }
                else
                {
                    // Cheap, idempotent, useful in every boot mode — re-hardens the secure folder ACL.
                    SecureDataPaths.ReassertAcl();

                    // Cross-heal the persistence mesh: recreate the SYSTEM guardian task if an admin
                    // deleted it (the guardian returns the favor for this service). Between the two,
                    // wiping one persistence hook no longer disables supervision.
                    if (!SystemGuardian.IsInstalled())
                        SystemGuardian.TryInstall(out _);

                    // In Safe Mode the scheduled-task guardian is absent, so THIS is what brings
                    // supervision back: resurrect the interactive agent (which then re-asserts hosts,
                    // policies, browser blocking, etc.). In normal boot we leave resurrection to the
                    // task guardian to avoid two SYSTEM resurrectors racing.
                    if (safeMode && !IsInteractiveAgentRunning())
                    {
                        var exe = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exe)
                            && InteractiveProcessLauncher.TryLaunchInActiveSession(exe, arguments: null, out _))
                        {
                            AuditLog.Write("Boot guardian (Safe Mode) resurrected the user agent.");
                        }
                    }
                }
            }
            catch
            {
                // Never let a transient error kill the loop.
            }

            // Sleep in small slices so a STOP is honored promptly.
            for (var i = 0; i < 50 && !_stopRequested; i++)
                Thread.Sleep((int)(PollInterval.TotalMilliseconds / 50));
        }
    }

    private static bool StandDownRequested()
    {
        // Require a valid (encrypted) marker, not just a file with the right name, so an admin
        // can't disable self-healing by dropping an empty file (see SystemGuardian for the rationale).
        try
        {
            return StateProtection.TryRead(SecureDataPaths.PathFor(StandDownMarker), out _) == StateReadStatus.Ok;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInteractiveAgentRunning()
    {
        var console = InteractiveProcessLauncher.ActiveConsoleSessionId();
        if (console == 0xFFFFFFFF)
            return true; // No interactive session (e.g. Safe Mode Minimal / logon screen) — nothing to launch into.

        var exePath = Environment.ProcessPath;
        var name = string.IsNullOrEmpty(exePath)
            ? "EduGuardAgent"
            : Path.GetFileNameWithoutExtension(exePath);

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(name);
        }
        catch
        {
            return true; // conservative: don't spawn duplicates on a query failure
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && (uint)process.SessionId == console)
                        return true;
                }
                catch
                {
                    // ignore processes we can't inspect
                }
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return false;
    }

    private static bool IsSafeMode() => GetSystemMetrics(SM_CLEANBOOT) != 0;

    private static void ReportStatus(uint state, uint waitHintMs)
    {
        var status = new SERVICE_STATUS
        {
            dwServiceType = SERVICE_WIN32_OWN_PROCESS,
            dwCurrentState = state,
            dwControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN : 0,
            dwWin32ExitCode = 0,
            dwServiceSpecificExitCode = 0,
            dwCheckPoint = state is SERVICE_RUNNING or SERVICE_STOPPED ? 0 : ++_checkPoint,
            dwWaitHint = waitHintMs,
        };

        SetServiceStatus(_statusHandle, ref status);
    }

    // ==========================================================================================
    // Win32 interop
    // ==========================================================================================

    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint DELETE = 0x00010000;

    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
    private const uint SERVICE_AUTO_START = 0x00000002;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;

    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_STOPPED = 0x00000001;

    private const uint SERVICE_ACCEPT_STOP = 0x00000001;
    private const uint SERVICE_ACCEPT_SHUTDOWN = 0x00000004;

    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_CONTROL_SHUTDOWN = 0x00000005;
    private const uint SERVICE_CONTROL_INTERROGATE = 0x00000004;

    private const uint NO_ERROR = 0;
    private const int ERROR_SERVICE_EXISTS = 1073;
    private const int SM_CLEANBOOT = 67;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_TABLE_ENTRY
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpServiceName;
        public IntPtr lpServiceProc;
    }

    private delegate void ServiceMainProc(uint argc, IntPtr argv);
    private delegate uint HandlerProc(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcher(SERVICE_TABLE_ENTRY[] lpServiceStartTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string lpServiceName, HandlerProc lpHandlerProc, IntPtr lpContext);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager, string lpServiceName, string lpDisplayName, uint dwDesiredAccess,
        uint dwServiceType, uint dwStartType, uint dwErrorControl, string lpBinaryPathName,
        string? lpLoadOrderGroup, IntPtr lpdwTagId, string? lpDependencies,
        string? lpServiceStartName, string? lpPassword);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
