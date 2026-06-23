using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

/// <summary>
/// Low-level keyboard hook that swallows system shortcuts while kiosk mode is active:
/// Windows keys, Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4, the menu/apps key, etc.
///
/// Note: Ctrl+Alt+Del (the Secure Attention Sequence) cannot be intercepted from user
/// space — that requires a Group Policy / credential-provider level change and is out of
/// scope here.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class KeyboardLockHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_SPACE = 0x20;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_F11 = 0x7A;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_APPS = 0x5D;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbDllHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Keep a reference so the delegate is not garbage collected while the hook is set.
    private readonly HookProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private volatile bool _active;
    private bool _disposed;

    public KeyboardLockHook() => _proc = HookCallback;

    public void Enable()
    {
        if (_active || _disposed)
            return;

        using var module = Process.GetCurrentProcess().MainModule;
        var moduleHandle = module is not null
            ? GetModuleHandle(module.ModuleName)
            : GetModuleHandle(null);

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
        _active = _hook != IntPtr.Zero;
    }

    public void Disable()
    {
        _active = false;
        var hook = _hook;
        _hook = IntPtr.Zero;
        if (hook != IntPtr.Zero)
            UnhookWindowsHookEx(hook);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _active)
            {
                var message = wParam.ToInt32();
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    var data = Marshal.PtrToStructure<KbDllHookStruct>(lParam);
                    if (ShouldBlock((int)data.vkCode))
                        return new IntPtr(1);
                }
            }
        }
        catch
        {
            // Never throw from a low-level hook — it can freeze keyboard input system-wide.
        }

        var chain = _hook;
        return chain != IntPtr.Zero
            ? CallNextHookEx(chain, nCode, wParam, lParam)
            : IntPtr.Zero;
    }

    private static bool ShouldBlock(int vkCode)
    {
        // Windows keys and the context-menu key.
        if (vkCode is VK_LWIN or VK_RWIN or VK_APPS)
            return true;

        var alt = IsKeyDown(0x12) || IsKeyDown(0xA4) || IsKeyDown(0xA5); // VK_MENU / LALT / RALT
        var ctrl = IsKeyDown(0x11) || IsKeyDown(0xA2) || IsKeyDown(0xA3); // VK_CONTROL / LCTRL / RCTRL
        var win = IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

        // Block common Win shortcuts (Task View, desktop, Run, Explorer, snap, etc.).
        if (win)
            return true;

        // Alt+Tab, Alt+Esc, Alt+F4, Alt+Space (system menu → Move/Size).
        if (alt && vkCode is VK_TAB or VK_ESCAPE or VK_F4 or VK_SPACE)
            return true;

        // Ctrl+Esc (opens Start).
        if (ctrl && vkCode == VK_ESCAPE)
            return true;

        // Browser / app fullscreen toggle.
        if (vkCode == VK_F11)
            return true;

        return false;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disable();
    }
}
