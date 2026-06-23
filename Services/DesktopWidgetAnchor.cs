using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EduGuardAgent.Services;

internal static class DesktopWidgetAnchor
{
    private const int GwlExstyle = -20;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private const int SwShow = 5;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    public static void AttachToDesktop(Window window)
    {
        var hwnd = EnsureHandle(window);
        ApplyExtendedStyles(hwnd);

        var defView = FindDesktopDefView();
        if (defView != IntPtr.Zero)
            new WindowInteropHelper(window).Owner = defView;

        EnsureVisible(hwnd);
    }

    public static void PlaceBottomRight(Window window, double marginPx = 18)
    {
        var area = SystemParameters.WorkArea;
        window.Left = area.Right - window.ActualWidth - marginPx;
        window.Top = area.Bottom - window.ActualHeight - marginPx;

        var hwnd = EnsureHandle(window);
        if (hwnd == IntPtr.Zero)
            return;

        var (scaleX, scaleY) = GetScale(window);
        var x = (int)((area.Right - window.ActualWidth - marginPx) * scaleX);
        var y = (int)((area.Bottom - window.ActualHeight - marginPx) * scaleY);
        var width = (int)(window.ActualWidth * scaleX);
        var height = (int)(window.ActualHeight * scaleY);

        SetWindowPos(
            hwnd,
            HwndTop,
            x,
            y,
            width,
            height,
            SwpNoActivate | SwpShowwindow);
    }

    public static void EnsureVisible(Window window) =>
        EnsureVisible(EnsureHandle(window));

    public static void EnsureVisible(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        ShowWindow(hwnd, SwShow);
    }

    private static IntPtr EnsureHandle(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        return helper.Handle;
    }

    private static (double X, double Y) GetScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
            return (1, 1);

        var matrix = source.CompositionTarget.TransformToDevice;
        return (matrix.M11, matrix.M22);
    }

    private static void ApplyExtendedStyles(IntPtr hwnd)
    {
        var exStyle = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, exStyle | WsExToolwindow | WsExNoActivate);
    }

    private static IntPtr FindDesktopDefView()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);

        IntPtr defView = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (child == IntPtr.Zero)
                return true;

            defView = child;
            return false;
        }, IntPtr.Zero);

        return defView;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
