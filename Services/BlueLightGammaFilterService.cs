using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using EduGuardAgent.Models;

namespace EduGuardAgent.Services;

/// <summary>
/// f.lux-style warmth via per-monitor gamma ramps (no overlay window).
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

    private readonly Dictionary<string, GammaRamp> _savedRamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _active;
    private BlueLightFilterPhase _phase = BlueLightFilterPhase.Off;

    public void SetActive(bool active, BlueLightFilterPhase phase = BlueLightFilterPhase.Off)
    {
        lock (_gate)
        {
            if (_active == active && (!_active || _phase == phase))
            {
                if (active)
                    ApplyToAllMonitors();
                return;
            }

            _active = active;
            _phase = active ? phase : BlueLightFilterPhase.Off;

            if (active)
                ApplyToAllMonitors();
            else
                RestoreAllMonitors();
        }
    }

    public void RefreshLayout()
    {
        lock (_gate)
        {
            if (!_active)
                return;

            ApplyToAllMonitors();
        }
    }

    public void StartMonitoringDisplayChanges() =>
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        lock (_gate)
            RestoreAllMonitors();
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

    private void ApplyToAllMonitors()
    {
        var (kelvin, brightness) = _phase switch
        {
            BlueLightFilterPhase.Early => (EarlyKelvin, EarlyBrightness),
            BlueLightFilterPhase.Late => (LateKelvin, LateBrightness),
            BlueLightFilterPhase.Lock => (LockKelvin, LockBrightness),
            _ => (ReferenceKelvin, 1.0),
        };
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
}
