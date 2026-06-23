using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace EduGuardAgent.Services;

/// <summary>
/// Kiosk mode as an <b>alternate desktop</b>: the EduGuard shell is the home screen (like a
/// wallpaper + launcher). Approved apps open in normal windows on top — the shell stays open
/// underneath, exactly like the Windows desktop behind application windows.
///
/// Bringing an app to the front and classifying it as "approved" is deliberately tolerant so a
/// legitimately-launched app is never minimised by the guard:
/// <list type="bullet">
/// <item>Win32 apps (Notepad, Chrome, Edge, …) are matched by executable name, full path, or
/// process-tree membership. Chrome/Edge hand their window to a child process, so the window is
/// found by enumerating top-level windows rather than trusting the launcher's MainWindowHandle.</item>
/// <item>UWP apps (Calculator, …) have their window hosted by <c>ApplicationFrameHost.exe</c>. The
/// real hosted process is resolved through the <c>Windows.UI.Core.CoreWindow</c> child and, once a
/// launch is observed, remembered for the session so the guard keeps it visible.</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class KioskService : IDisposable
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const int SW_FORCEMINIMIZE = 11;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const string UwpHostProcess = "ApplicationFrameHost";
    private const string UwpFrameClass = "ApplicationFrameWindow";
    private const string UwpCoreWindowClass = "Windows.UI.Core.CoreWindow";

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private static readonly TimeSpan GuardInterval = TimeSpan.FromMilliseconds(Config.KioskGuardIntervalMs);
    private static readonly TimeSpan LaunchGrace = TimeSpan.FromSeconds(12);

    private readonly KioskAppRegistry _registry;
    private readonly KeyboardLockHook _keyboard = new();
    private readonly object _lock = new();

    // PIDs we launched (roots). Their descendants are computed from the process tree each guard tick.
    private readonly HashSet<int> _rootPids = [];

    // Process names discovered to belong to an app we launched (e.g. a UWP host's real process,
    // or a child process name). Allowed for the rest of the kiosk session.
    private readonly HashSet<string> _runtimeAllowedNames = new(StringComparer.OrdinalIgnoreCase);

    private Timer? _guardTimer;
    private IntPtr _kioskWindow = IntPtr.Zero;
    private volatile bool _active;
    private volatile bool _guardBusy;
    private long _launchGraceUntilTicks;
    private bool _disposed;

    public KioskService(KioskAppRegistry registry) => _registry = registry;

    public event Action<string>? Log;

    public bool IsActive => _active;

    public void SetKioskWindow(IntPtr handle) => _kioskWindow = handle;

    public void Activate()
    {
        lock (_lock)
        {
            if (_active || _disposed)
                return;

            _active = true;
            _keyboard.Enable();
            SetTaskbarVisible(false);
            _guardTimer = new Timer(_ => GuardTick(), null, GuardInterval, GuardInterval);
            Log?.Invoke("Kiosk desktop activated.");
        }
    }

    public void Deactivate() => ForceRelease();

    /// <summary>Always restores taskbar and keyboard, even if kiosk state is inconsistent.</summary>
    public void ForceRelease()
    {
        lock (_lock)
        {
            _active = false;
            _guardTimer?.Dispose();
            _guardTimer = null;
            _keyboard.Disable();
            SetTaskbarVisible(true);
            _rootPids.Clear();
            _runtimeAllowedNames.Clear();
            Interlocked.Exchange(ref _launchGraceUntilTicks, 0);
            Log?.Invoke("Kiosk desktop deactivated.");
        }
    }

    public bool LaunchApp(KioskApp app)
    {
        var normalizedPath = app.NormalizedPath;
        var targetName = Path.GetFileNameWithoutExtension(normalizedPath);

        Interlocked.Exchange(ref _launchGraceUntilTicks, (DateTimeOffset.UtcNow + LaunchGrace).UtcTicks);

        try
        {
            if (!File.Exists(app.Path))
            {
                Log?.Invoke($"Kiosk could not launch {app.Name}: file not found ({app.Path}).");
                return false;
            }

            // Snapshot UWP windows already open so we can tell which one is "ours" after launch.
            var uwpBefore = SnapshotUwpHostedPids();

            if (TryFocusWindowByProcessName(targetName))
            {
                RegisterRuntimeName(targetName);
                Log?.Invoke($"Kiosk brought {app.Name} to the front.");
                return true;
            }

            var startInfo = new ProcessStartInfo(app.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(app.Path) ?? string.Empty,
            };

            var args = BuildLaunchArgs(app.Path, app.Args);
            if (!string.IsNullOrWhiteSpace(args))
                startInfo.Arguments = args;

            var process = Process.Start(startInfo);
            RegisterProcess(process);
            ScheduleFocusApp(process, targetName, uwpBefore);

            Log?.Invoke($"Kiosk opened {app.Name} on the alternate desktop.");
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Kiosk could not launch {app.Name}: {ex.Message}");
            return false;
        }
    }

    private void GuardTick()
    {
        if (!_active || _guardBusy)
            return;

        _guardBusy = true;
        try
        {
            ReassertTaskbarHidden();

            var foreground = GetForegroundWindow();

            if (foreground == IntPtr.Zero || IsDesktopShellWindow(foreground))
            {
                FocusKioskShell();
                return;
            }

            if (foreground == _kioskWindow)
                return;

            GetWindowThreadProcessId(foreground, out var pid);
            if (pid == 0)
            {
                FocusKioskShell();
                return;
            }

            if (pid == (uint)Environment.ProcessId)
                return;

            if (IsAllowedForeground(foreground, (int)pid))
                return;

            ShowWindow(foreground, SW_FORCEMINIMIZE);
            FocusKioskShell();
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            _guardBusy = false;
        }
    }

    private void FocusKioskShell()
    {
        if (_kioskWindow == IntPtr.Zero)
            return;

        ShowWindow(_kioskWindow, SW_RESTORE);
        SetForegroundWindow(_kioskWindow);
    }

    private void ReassertTaskbarHidden() => SetTaskbarVisible(false);

    private static bool IsDesktopShellWindow(IntPtr hwnd)
    {
        var cls = GetWindowClass(hwnd);
        return string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterProcess(Process? process)
    {
        if (process is null)
            return;

        try
        {
            lock (_lock)
                _rootPids.Add(process.Id);

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                lock (_lock)
                    _rootPids.Remove(process.Id);
            };
        }
        catch
        {
            // Best-effort.
        }
    }

    private void ScheduleFocusApp(Process? process, string targetName, HashSet<int> uwpBefore)
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 36; attempt++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (!_active)
                    return;

                // 1) A normal top-level window owned by a process with the expected name
                //    (covers Chrome/Edge whose window belongs to a child process).
                if (TryFocusWindowByProcessName(targetName))
                {
                    RegisterRuntimeName(targetName);
                    return;
                }

                // 2) The launcher process exposes a main window directly (Notepad, …).
                if (process is { HasExited: false })
                {
                    try
                    {
                        process.Refresh();
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            BringToFront(process.MainWindowHandle, IsBrowserProcess(targetName));
                            return;
                        }
                    }
                    catch
                    {
                        // Process may have handed off to a child.
                    }
                }

                // 3) A newly opened UWP window (Calculator, …) hosted by ApplicationFrameHost.
                if (TryFocusNewUwpWindow(uwpBefore))
                    return;
            }
        });
    }

    /// <summary>
    /// Allowed when the foreground window belongs to an approved app. UWP windows are hosted by
    /// <c>ApplicationFrameHost.exe</c>, so the real hosted process is resolved and checked against the
    /// names we observed at launch; during the launch grace the host is tolerated while we register it.
    /// </summary>
    private bool IsAllowedForeground(IntPtr hwnd, int pid)
    {
        var name = TryGetProcessName(pid);
        if (name is null)
            return false;

        if (string.Equals(name, UwpHostProcess, StringComparison.OrdinalIgnoreCase))
        {
            var hosted = ResolveUwpHostedProcessName(hwnd);
            if (hosted is not null)
            {
                if (IsRuntimeAllowed(hosted) || _registry.IsApprovedProcessName(hosted))
                    return true;
            }

            // Brief tolerance while a Dom-approved UWP app is still starting.
            return DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _launchGraceUntilTicks)
                   && hosted is not null;
        }

        if (_registry.IsApprovedProcessName(name) || IsRuntimeAllowed(name))
            return true;

        var path = TryGetProcessPath(pid);
        if (path is not null && _registry.IsApprovedPath(path))
            return true;

        return IsInLaunchedTree(pid);
    }

    private bool TryFocusWindowByProcessName(string processName)
    {
        var target = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0)
                return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
                return true;

            var name = TryGetProcessName((int)pid);
            if (name is not null && string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
            {
                lock (_lock)
                    _rootPids.Add((int)pid);
                target = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero)
            return false;

        BringToFront(target, IsBrowserProcess(processName));
        return true;
    }

    private bool TryFocusNewUwpWindow(HashSet<int> uwpBefore)
    {
        var target = IntPtr.Zero;
        string? hostedName = null;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0)
                return true;

            if (!string.Equals(GetWindowClass(hWnd), UwpFrameClass, StringComparison.OrdinalIgnoreCase))
                return true;

            var coreWindow = FindWindowEx(hWnd, IntPtr.Zero, UwpCoreWindowClass, null);
            if (coreWindow == IntPtr.Zero)
                return true;

            GetWindowThreadProcessId(coreWindow, out var realPid);
            if (realPid == 0 || uwpBefore.Contains((int)realPid))
                return true;

            hostedName = TryGetProcessName((int)realPid);
            target = hWnd;
            return false;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero || hostedName is null)
            return false;

        RegisterRuntimeName(hostedName);
        BringToFront(target);
        return true;
    }

    private HashSet<int> SnapshotUwpHostedPids()
    {
        var pids = new HashSet<int>();
        EnumWindows((hWnd, _) =>
        {
            if (!string.Equals(GetWindowClass(hWnd), UwpFrameClass, StringComparison.OrdinalIgnoreCase))
                return true;

            var coreWindow = FindWindowEx(hWnd, IntPtr.Zero, UwpCoreWindowClass, null);
            if (coreWindow == IntPtr.Zero)
                return true;

            GetWindowThreadProcessId(coreWindow, out var realPid);
            if (realPid != 0)
                pids.Add((int)realPid);

            return true;
        }, IntPtr.Zero);

        return pids;
    }

    private static void BringToFront(IntPtr hWnd, bool normalizeBrowser = false)
    {
        ShowWindow(hWnd, SW_RESTORE);
        if (normalizeBrowser)
            ResizeBrowserIfNearlyFullscreen(hWnd);
        SetForegroundWindow(hWnd);
    }

    private static void ResizeBrowserIfNearlyFullscreen(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out var rect))
            return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var screenWidth = GetSystemMetrics(SmCxScreen);
        var screenHeight = GetSystemMetrics(SmCyScreen);

        if (width >= screenWidth - 80 || height >= screenHeight - 80 || IsZoomed(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
            SetWindowPos(hWnd, IntPtr.Zero, 80, 60, 1200, 800, SWP_SHOWWINDOW);
        }
    }

    private void RegisterRuntimeName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return;

        lock (_lock)
            _runtimeAllowedNames.Add(processName);
    }

    private bool IsRuntimeAllowed(string processName)
    {
        lock (_lock)
            return _runtimeAllowedNames.Contains(processName);
    }

    private bool IsInLaunchedTree(int pid)
    {
        int[] roots;
        lock (_lock)
        {
            if (_rootPids.Count == 0)
                return false;
            if (_rootPids.Contains(pid))
                return true;
            roots = [.. _rootPids];
        }

        var parents = BuildParentMap();
        if (parents.Count == 0)
            return false;

        var rootSet = new HashSet<int>(roots);
        var current = pid;
        for (var depth = 0; depth < 16; depth++)
        {
            if (!parents.TryGetValue(current, out var parent) || parent == 0 || parent == current)
                return false;
            if (rootSet.Contains(parent))
                return true;
            current = parent;
        }

        return false;
    }

    private static Dictionary<int, int> BuildParentMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return map;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry))
                return map;

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
            while (Process32Next(snapshot, ref entry));
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    /// <summary>
    /// Resolves the real process name of the UWP app hosted by an <c>ApplicationFrameWindow</c> via its
    /// <c>Windows.UI.Core.CoreWindow</c> child, since the frame itself is owned by ApplicationFrameHost.
    /// </summary>
    private static string? ResolveUwpHostedProcessName(IntPtr frameWindow)
    {
        if (!string.Equals(GetWindowClass(frameWindow), UwpFrameClass, StringComparison.OrdinalIgnoreCase))
            return null;

        var coreWindow = FindWindowEx(frameWindow, IntPtr.Zero, UwpCoreWindowClass, null);
        if (coreWindow == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(coreWindow, out var realPid);
        return realPid == 0 ? null : TryGetProcessName((int)realPid);
    }

    private static string GetWindowClass(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private static string? TryGetProcessPath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strip fullscreen/kiosk flags and add windowed defaults for browsers.</summary>
    private static string BuildLaunchArgs(string exePath, string? userArgs)
    {
        var tokens = string.IsNullOrWhiteSpace(userArgs)
            ? []
            : userArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !IsFullscreenArg(token))
                .ToList();

        var processName = Path.GetFileNameWithoutExtension(exePath);
        if (IsBrowserProcess(processName))
        {
            tokens.Add("--new-window");
            tokens.Add("--window-size=1200,800");
            tokens.Add("--window-position=80,60");
        }

        return tokens.Count == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private static bool IsBrowserProcess(string processName) =>
        string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase)
        || string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase);

    private static bool IsFullscreenArg(string token)
    {
        if (token.StartsWith("--app=", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("-app=", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("/app=", StringComparison.OrdinalIgnoreCase))
            return true;

        return token.Equals("--kiosk", StringComparison.OrdinalIgnoreCase)
            || token.Equals("-kiosk", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/kiosk", StringComparison.OrdinalIgnoreCase)
            || token.Equals("--start-fullscreen", StringComparison.OrdinalIgnoreCase)
            || token.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase)
            || token.Equals("--start-maximized", StringComparison.OrdinalIgnoreCase)
            || token.Equals("-start-maximized", StringComparison.OrdinalIgnoreCase);
    }

    private void SetTaskbarVisible(bool visible)
    {
        try
        {
            foreach (var className in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                var tray = FindWindow(className, null);
                if (tray != IntPtr.Zero)
                    ShowWindow(tray, visible ? SW_SHOW : SW_HIDE);
            }

            var startButton = FindWindow("Button", "Start");
            if (startButton != IntPtr.Zero)
                ShowWindow(startButton, visible ? SW_SHOW : SW_HIDE);
        }
        catch
        {
            // Best-effort.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ForceRelease();
        _keyboard.Dispose();
    }
}
