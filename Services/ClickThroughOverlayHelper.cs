using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EduGuardAgent.Services;

internal static class ClickThroughOverlayHelper
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const int WmNchittest = 0x0084;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HtTransparent = new(-1);

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public static void Apply(Window window, int physicalX, int physicalY, int physicalWidth, int physicalHeight)
    {
        var hwnd = EnsureHandle(window);
        if (hwnd == IntPtr.Zero)
            return;

        var scale = GetScale(hwnd);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = physicalX / scale;
        window.Top = physicalY / scale;
        window.Width = Math.Max(1, physicalWidth / scale);
        window.Height = Math.Max(1, physicalHeight / scale);

        var exStyle = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(
            hwnd,
            GwlExstyle,
            exStyle | WsExTransparent | WsExLayered | WsExToolwindow | WsExNoActivate);

        SetWindowPos(
            hwnd,
            HwndTopmost,
            physicalX,
            physicalY,
            physicalWidth,
            physicalHeight,
            SwpNoActivate | SwpShowwindow | SwpFrameChanged | SwpNoOwnerZOrder);
    }

    public static void AttachTransparentHitTest(Window window)
    {
        void Hook()
        {
            var hwnd = EnsureHandle(window);
            if (hwnd == IntPtr.Zero)
                return;

            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(TransparentHitTestHook);
        }

        if (window.IsLoaded)
            Hook();
        else
            window.SourceInitialized += (_, _) => Hook();
    }

    private static IntPtr TransparentHitTestHook(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmNchittest)
        {
            handled = true;
            return HtTransparent;
        }

        return IntPtr.Zero;
    }

    private static double GetScale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is not null)
                return source.CompositionTarget.TransformToDevice.M11;
        }

        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static IntPtr EnsureHandle(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        return helper.Handle;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private static int GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? (int)GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLong32(hWnd, nIndex);

    private static void SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
        else
            SetWindowLong32(hWnd, nIndex, dwNewLong);
    }
}
