using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using EduGuardAgent.Models;

namespace EduGuardAgent.Services;

/// <summary>
/// f.lux-style warmth without an overlay window.
///
/// Primary path: a DWM composition-level colour matrix via the Windows Magnification API
/// (<c>MagSetFullscreenColorEffect</c>). The transform is applied to the final composed
/// frame, so — unlike a per-adapter GDI gamma ramp — it is NOT torn down when another app
/// takes the foreground or spins up a Direct3D swap-chain. That composition-level stability
/// is what fixes the "opening almost any app flashes the filter off" problem: gamma ramps
/// are a shared, unprotected piece of driver state that countless apps reset, and any
/// reactive reassert (timer/foreground hook) inherently loses the race and shows a flash.
///
/// Fallback path: if the Magnification effect is unavailable, we fall back to per-monitor
/// GDI gamma ramps with an aggressive reassert (kept only for that fallback). Both paths are
/// fully reversible — the effect is removed on Dispose and when the process exits.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class BlueLightGammaFilterService : IDisposable
{
    private const double ReferenceKelvin = 6500;
    private const double EarlyKelvin = 3600;
    private const double EarlyBrightness = 0.94;
    private const double LateKelvin = 3100;
    private const double LateBrightness = 0.91;
    private const double LockKelvin = 2600;
    private const double LockBrightness = 0.87;

    private static readonly TimeSpan ReassertInterval = TimeSpan.FromMilliseconds(300);

    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private bool _active;
    private BlueLightFilterPhase _phase = BlueLightFilterPhase.Off;

    // Magnification (primary) state.
    private bool _magInitialized;
    private bool _magUnavailable;

    // Gamma (fallback) state — only used if the Magnification effect can't be applied.
    private readonly Dictionary<string, GammaRamp> _savedRamps = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _reassertTimer;
    private WinEventProc? _foregroundHookProc;
    private IntPtr _foregroundHook;
    private bool _gammaActive;

    public BlueLightGammaFilterService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void SetActive(bool active, BlueLightFilterPhase phase = BlueLightFilterPhase.Off)
    {
        lock (_gate)
        {
            _active = active;
            _phase = active ? phase : BlueLightFilterPhase.Off;
        }

        Apply();
    }

    public void RefreshLayout()
    {
        lock (_gate)
        {
            if (!_active)
                return;
        }

        Apply();
    }

    public void StartMonitoringDisplayChanges()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        // Reassert around the events that can drop either backend (display mode change,
        // resume from sleep, session unlock). Cheap and relevant to both paths.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        _foregroundHookProc = null;

        lock (_gate)
        {
            _reassertTimer?.Dispose();
            _reassertTimer = null;
            RestoreAllMonitors();
        }

        TeardownMagnification();
    }

    private void Apply()
    {
        bool active;
        BlueLightFilterPhase phase;
        lock (_gate)
        {
            active = _active;
            phase = _phase;
        }

        // Filter is off and nothing was ever applied — don't spin up any backend.
        if (!active && !_magInitialized && !_gammaActive)
            return;

        if (!_magUnavailable)
        {
            if (TryApplyMagnification(active, phase))
                return;

            // One-time, permanent fallback for this session — don't keep retrying a
            // backend the machine clearly doesn't support.
            _magUnavailable = true;
        }

        ApplyGamma(active);
    }

    // --- Magnification (primary) ---------------------------------------------------------

    private bool TryApplyMagnification(bool active, BlueLightFilterPhase phase)
    {
        // MagInitialize / MagSetFullscreenColorEffect are affine to the thread that owns the
        // message pump; run them on the UI dispatcher thread.
        if (!_dispatcher.CheckAccess())
        {
            try
            {
                return _dispatcher.Invoke(() => TryApplyMagnification(active, phase));
            }
            catch (TaskCanceledException)
            {
                return _magInitialized; // app shutting down — treat as no-op
            }
        }

        try
        {
            if (!_magInitialized)
            {
                if (!MagInitialize())
                    return false;

                _magInitialized = true;
            }

            var effect = BuildColorEffect(active ? phase : BlueLightFilterPhase.Off);
            return MagSetFullscreenColorEffect(ref effect);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private void TeardownMagnification()
    {
        if (!_magInitialized)
            return;

        void Reset()
        {
            try
            {
                var identity = BuildColorEffect(BlueLightFilterPhase.Off);
                MagSetFullscreenColorEffect(ref identity);
                MagUninitialize();
            }
            catch
            {
                // Best effort — the effect is dropped when the process exits regardless.
            }
            finally
            {
                _magInitialized = false;
            }
        }

        if (_dispatcher.CheckAccess())
            Reset();
        else
        {
            try { _dispatcher.Invoke(Reset); }
            catch { /* shutting down */ }
        }
    }

    private static MagColorEffect BuildColorEffect(BlueLightFilterPhase phase)
    {
        var (kelvin, brightness) = PhaseParams(phase);
        GetChannelMultipliers(kelvin, out var r, out var g, out var b);

        // 5x5 colour matrix, row-major. Colour is a row vector [R G B A 1] multiplied on the
        // left, so per-channel scaling lives on the diagonal. Brightness dims all channels.
        var t = new float[25];
        t[0] = (float)(r * brightness);   // R -> R
        t[6] = (float)(g * brightness);   // G -> G
        t[12] = (float)(b * brightness);  // B -> B
        t[18] = 1f;                       // A -> A
        t[24] = 1f;                       // translation row
        return new MagColorEffect { Transform = t };
    }

    // --- Gamma ramp (fallback) -----------------------------------------------------------

    private void ApplyGamma(bool active)
    {
        lock (_gate)
        {
            if (active)
            {
                ApplyToAllMonitors();
                _gammaActive = true;
                EnsureGammaWatchers();
            }
            else
            {
                RestoreAllMonitors();
                _gammaActive = false;
            }
        }
    }

    /// <summary>
    /// The reassert timer + foreground hook only matter for the racy gamma fallback; the
    /// Magnification path is stable and never needs them, so they're created lazily.
    /// </summary>
    private void EnsureGammaWatchers()
    {
        _reassertTimer ??= new Timer(_ => ReassertGammaIfActive(), null, ReassertInterval, ReassertInterval);

        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundHookProc = OnForegroundChanged;
            _foregroundHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _foregroundHookProc,
                0,
                0,
                WineventOutofcontext);
        }
    }

    private void ReassertGammaIfActive()
    {
        lock (_gate)
        {
            if (_gammaActive)
                ApplyToAllMonitors();
        }
    }

    private void ApplyToAllMonitors()
    {
        var (kelvin, brightness) = PhaseParams(_phase);
        var warmRamp = GammaRamp.FromColorTemperature(kelvin, brightness);

        foreach (var device in EnumerateMonitorDevices())
            ApplyRamp(device, warmRamp);
    }

    private void RestoreAllMonitors()
    {
        foreach (var device in EnumerateMonitorDevices())
            RestoreRamp(device);

        _savedRamps.Clear();
    }

    private void ApplyRamp(string deviceName, GammaRamp warmRamp)
    {
        var hdc = CreateDc(null, deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            if (!_savedRamps.ContainsKey(deviceName))
            {
                var current = GammaRamp.CreateEmpty();
                if (!GetDeviceGammaRamp(hdc, ref current))
                    return;

                _savedRamps[deviceName] = current;
            }

            SetDeviceGammaRamp(hdc, ref warmRamp);
        }
        finally
        {
            DeleteDc(hdc);
        }
    }

    private void RestoreRamp(string deviceName)
    {
        if (!_savedRamps.TryGetValue(deviceName, out var original))
            return;

        var hdc = CreateDc(null, deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            var ramp = original;
            SetDeviceGammaRamp(hdc, ref ramp);
        }
        finally
        {
            DeleteDc(hdc);
        }
    }

    // --- Shared event handling -----------------------------------------------------------

    private void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) =>
        ReassertGammaIfActive();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Apply();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Apply();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock
            or SessionSwitchReason.SessionLogon
            or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.ConsoleConnect)
            Apply();
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "WorkArea"
            or "PrimaryScreenWidth"
            or "PrimaryScreenHeight"
            or "VirtualScreenWidth"
            or "VirtualScreenHeight")
        {
            RefreshLayout();
        }
    }

    // --- Colour maths (shared by both backends) ------------------------------------------

    private static (double Kelvin, double Brightness) PhaseParams(BlueLightFilterPhase phase) => phase switch
    {
        BlueLightFilterPhase.Early => (EarlyKelvin, EarlyBrightness),
        BlueLightFilterPhase.Late => (LateKelvin, LateBrightness),
        BlueLightFilterPhase.Lock => (LockKelvin, LockBrightness),
        _ => (ReferenceKelvin, 1.0),
    };

    private static void GetChannelMultipliers(double kelvin, out double r, out double g, out double b)
    {
        KelvinToRgb(kelvin, out var tr, out var tg, out var tb);
        KelvinToRgb(ReferenceKelvin, out var dr, out var dg, out var db);

        r = Math.Min(1.0, tr / dr);
        g = Math.Min(1.0, tg / dg);
        b = Math.Min(1.0, tb / db);
    }

    private static void KelvinToRgb(double kelvin, out double r, out double g, out double b)
    {
        var temp = Math.Clamp(kelvin, 1000, 40000) / 100.0;

        if (temp <= 66)
            r = 255;
        else
            r = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);

        if (temp <= 66)
            g = 99.4708025861 * Math.Log(temp) - 161.1195681661;
        else
            g = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);

        if (temp >= 66)
            b = 255;
        else if (temp <= 19)
            b = 0;
        else
            b = 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;

        r = Math.Clamp(r, 0, 255) / 255.0;
        g = Math.Clamp(g, 0, 255) / 255.0;
        b = Math.Clamp(b, 0, 255) / 255.0;
    }

    // --- Interop -------------------------------------------------------------------------

    private static List<string> EnumerateMonitorDevices()
    {
        var devices = new List<string>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMonitor, IntPtr _, ref NativeRect __, IntPtr ___) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(hMonitor, ref info) && !string.IsNullOrWhiteSpace(info.DeviceName))
                    devices.Add(info.DeviceName);
                return true;
            },
            IntPtr.Zero);
        return devices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MagColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] Transform;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;

        public static GammaRamp CreateEmpty() =>
            new()
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256],
            };

        public static GammaRamp FromColorTemperature(double kelvin, double brightness)
        {
            GetChannelMultipliers(kelvin, out var rMult, out var gMult, out var bMult);

            var ramp = CreateEmpty();
            for (var i = 0; i < 256; i++)
            {
                var identity = (ushort)(i * 257);
                ramp.Red[i] = Scale(identity, rMult, brightness);
                ramp.Green[i] = Scale(identity, gMult, brightness);
                ramp.Blue[i] = Scale(identity, bMult, brightness);
            }

            return ramp;
        }

        private static ushort Scale(ushort identity, double multiplier, double brightness) =>
            (ushort)Math.Clamp((int)(identity * multiplier * brightness), 0, 65535);
    }

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        ref NativeRect lprcMonitor,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateDC")]
    private static extern IntPtr CreateDc(
        string? lpszDriver,
        string? lpszDevice,
        string? lpszOutput,
        IntPtr lpInitData);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")]
    private static extern bool DeleteDc(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool GetDeviceGammaRamp(IntPtr hDC, ref GammaRamp lpRamp);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref GammaRamp lpRamp);

    [DllImport("Magnification.dll")]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll")]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll")]
    private static extern bool MagSetFullscreenColorEffect(ref MagColorEffect pEffect);

    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutofcontext = 0x0000;

    private delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
