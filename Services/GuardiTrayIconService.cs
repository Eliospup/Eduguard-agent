using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using EduGuardAgent.Profiles;
using EduGuardAgent.Views;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal sealed class GuardiTrayIconService : IDisposable
{
    private const int WmUser = 0x0400;
    private const int WmTrayCallback = WmUser + 1;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NifInfo = 0x00000010;
    private const int NiInfo = 0x00000001;

    private readonly Action _onOpen;
    private readonly Action _onQuit;
    private readonly Icon _icon;
    private readonly nint _iconHandle;
    private readonly nint _windowHandle;
    private readonly HwndSource _messageWindow;
    private bool _disposed;

    public GuardiTrayIconService(Action onOpen, Action onQuit)
    {
        _onOpen = onOpen;
        _onQuit = onQuit;
        _icon = CreateTrayIcon(out _iconHandle);

        var parameters = new HwndSourceParameters("GuardiTrayMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = nint.Zero,
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
        _windowHandle = _messageWindow.Handle;

        var data = CreateNotifyData();
        if (!Shell_NotifyIcon(NimAdd, ref data))
            throw new InvalidOperationException("Failed to create Guardi tray icon.");
    }

    public void ShowBalloon(string title, string message)
    {
        var data = CreateNotifyData();
        data.uFlags = NifInfo | NifTip;
        data.dwInfoFlags = NiInfo;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        Shell_NotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var data = CreateNotifyData();
        Shell_NotifyIcon(NimDelete, ref data);
        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
        _icon.Dispose();
        if (_iconHandle != nint.Zero)
            DestroyIcon(_iconHandle);
    }

    private NotifyIconData CreateNotifyData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _windowHandle,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = WmTrayCallback,
        hIcon = _icon.Handle,
        szTip = Truncate(UiCopy.TrayTooltip, 127),
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmTrayCallback)
            return IntPtr.Zero;

        switch ((int)lParam)
        {
            case WmLButtonDblClk:
                _onOpen();
                handled = true;
                break;

            case WmRButtonUp:
                ShowGuardiMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowGuardiMenu()
    {
        if (!GetCursorPos(out var point))
            return;

        SetForegroundWindow(_windowHandle);
        GuardiTrayMenuWindow.ShowNear(point.X, point.Y, _onOpen, _onQuit);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static Icon CreateTrayIcon(out nint handle)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(37, 99, 235));
            graphics.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new System.Drawing.Font("Segoe UI", 13f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.White);
            var size = graphics.MeasureString("G", font);
            graphics.DrawString(
                "G",
                font,
                text,
                (32f - size.Width) / 2f,
                (32f - size.Height) / 2f - 1f);
        }

        handle = bitmap.GetHicon();
        using var fromHandle = Icon.FromHandle(handle);
        return (Icon)fromHandle.Clone();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;
        public int dwInfoFlags;

        public Guid guidItem;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlagsEx;
        public int uTimeoutOrVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
