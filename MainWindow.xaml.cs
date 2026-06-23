using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;
using EduGuardAgent.Services;
using EduGuardAgent.ViewModels;
using EduGuardAgent.Views;

namespace EduGuardAgent;

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly BlueLightGammaFilterService _blueLightFilter = new();
    private readonly System.Collections.Generic.List<LockOverlayWindow> _lockOverlays = new();
    private DomMessageOverlayWindow? _domMessageOverlay;
    private AppBlockedToastWindow? _appBlockedToast;
    private BedtimeWarningToastWindow? _bedtimeWarningToast;
    private GamingHudOverlayWindow? _gamingHud;
    private GamingSessionToastWindow? _gamingSessionToast;
    private YoutubeHudOverlayWindow? _youtubeHud;
    private ExtensionInstallOverlayWindow? _extensionOverlay;
    private GuardiWidgetWindow? _guardiWidget;
    private GuardiTrayIconService? _trayIcon;
    private bool _trayBalloonShown;
    private WindowState _preKioskWindowState = WindowState.Normal;
    private ResizeMode _preKioskResizeMode = ResizeMode.CanResize;
    private bool _preKioskShowInTaskbar = true;
    private double _preKioskLeft;
    private double _preKioskTop;
    private double _preKioskWidth;
    private double _preKioskHeight;
    private DispatcherTimer? _kioskShellWatchTimer;
    private HwndSource? _kioskHwndSource;
    private HwndSource? _chromeHwndSource;
    private KioskBackgroundCurtainWindow? _kioskCurtain;
    private bool _forceClose;
    private bool _closeDialogOpen;
    private bool _isQuitting;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.ScreenTimeLocked += OnLockOverlayRequested;
        _viewModel.ScreenTimeLockDismissed += OnLockOverlayDismissed;
        _viewModel.LockOverlayChanged += OnLockOverlayChanged;
        _viewModel.DomMessagePopupRequested += OnDomMessagePopupRequested;
        _viewModel.AppBlockedPopupRequested += OnAppBlockedPopupRequested;
        _viewModel.BedtimeWarningPopupRequested += OnBedtimeWarningPopupRequested;
        _viewModel.StudyToastPopupRequested += OnStudyToastPopupRequested;
        _viewModel.BlueLightFilterStateChanged += OnBlueLightFilterStateChanged;
        _viewModel.GamingHudStateChanged += OnGamingHudStateChanged;
        _viewModel.GamingSessionToastRequested += OnGamingSessionToastRequested;
        _viewModel.YoutubeHudStateChanged += OnYoutubeHudStateChanged;
        _viewModel.ExtensionGuardStateChanged += OnExtensionGuardStateChanged;
        _viewModel.FirefoxRestartToastRequested += OnFirefoxRestartToastRequested;
        _viewModel.BrowserRestartCountdownRequested += OnBrowserRestartCountdownRequested;
        _viewModel.ModePresentationChanged += OnModePresentationChanged;
        _viewModel.KioskStateChanged += OnKioskStateChanged;
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            _chromeHwndSource = HwndSource.FromHwnd(hwnd);
            _chromeHwndSource?.AddHook(ChromeWndProc);
        }

        ApplyWindowChrome();
        UpdateWindowRoundness();
        if (_viewModel.IsKioskActive)
            RegisterKioskHandle();
    }

    private void UpdateWindowRoundness()
    {
        if (WindowShell is null)
            return;

        var rounded = !_viewModel.IsKioskActive && WindowState != WindowState.Maximized;
        var radius = rounded ? ResolveWindowCornerRadius() : 0.0;
        WindowShell.CornerRadius = new CornerRadius(radius);
        WindowShell.BorderThickness = rounded ? new Thickness(1) : new Thickness(0);
        WindowShell.Margin = rounded ? new Thickness(10) : new Thickness(0);
        WindowShell.Effect = rounded
            ? Application.Current?.Resources["WindowShellShadow"] as System.Windows.Media.Effects.Effect
            : null;

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero)
                return;

            const int DwmwaWindowCornerPreference = 33;
            const int DwmwcpDoNotRound = 1;
            const int DwmwcpRound = 2;
            int preference = rounded ? DwmwcpRound : DwmwcpDoNotRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            ApplyWindowChrome();
        }
        catch
        {
            // Optional cosmetic on older Windows builds.
        }
    }

    private void EnsureDashboardVisible()
    {
        if (FindName("DashboardReveal") is not System.Windows.Controls.Grid dashboard)
            return;

        dashboard.Opacity = 1;
        if (dashboard.RenderTransform is System.Windows.Media.TranslateTransform transform)
            transform.Y = 0;
    }

    private void EnsureKioskBounds()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        UpdateWindowRoundness();
    }

    private static double ResolveWindowCornerRadius()
    {
        if (Application.Current?.Resources["WindowCornerRadius"] is CornerRadius radius)
            return radius.TopLeft;

        return 24;
    }

    private void ApplyWindowChrome()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero)
                return;

            var chromeColor = ResolvePageGradientTopBgr();

            const int DwmwaNcRenderingPolicy = 2;
            const int DwmncrpDisabled = 1;
            int ncPolicy = DwmncrpDisabled;
            _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref ncPolicy, sizeof(int));

            const int DwmwaBorderColor = 34;
            const int DwmwaCaptionColor = 35;
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref chromeColor, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref chromeColor, sizeof(int));
        }
        catch
        {
            // Optional cosmetic on older Windows builds.
        }
    }

    private static int ResolvePageGradientTopBgr()
    {
        if (Application.Current?.Resources["PageGradient"] is LinearGradientBrush brush
            && brush.GradientStops.Count > 0)
        {
            var color = brush.GradientStops[0].Color;
            return (color.B << 16) | (color.G << 8) | color.R;
        }

        return 0x004E3B06; // #064E3B
    }

    private IntPtr ChromeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmNcCalcSize = 0x0083;
        if (msg != WmNcCalcSize || wParam == nint.Zero || _viewModel.IsKioskActive)
            return nint.Zero;

        if (WindowState == WindowState.Maximized)
        {
            var nc = Marshal.PtrToStructure<NcCalcSizeParams>(lParam);
            var border = GetResizeBorderThickness();
            nc.Rgrc0.Top += (int)Math.Round(border.Top);
            nc.Rgrc0.Left += (int)Math.Round(border.Left);
            nc.Rgrc0.Right -= (int)Math.Round(border.Right);
            nc.Rgrc0.Bottom -= (int)Math.Round(border.Bottom);
            Marshal.StructureToPtr(nc, lParam, false);
        }

        handled = true;
        return nint.Zero;
    }

    private Thickness GetResizeBorderThickness()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        var dpi = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var padded = GetSystemMetrics(SmCxPaddedBorder);
        var frameX = GetSystemMetrics(SmCxFrame);
        var frameY = GetSystemMetrics(SmCyFrame);
        var horizontal = (frameX + padded) / dpi;
        var vertical = (frameY + padded) / dpi;
        return new Thickness(horizontal, vertical, horizontal, vertical);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SmCxFrame = 32;
    private const int SmCyFrame = 33;
    private const int SmCxPaddedBorder = 92;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams
    {
        public Rect Rgrc0;
        public Rect Rgrc1;
        public Rect Rgrc2;
    }

    private void RegisterKioskHandle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            _viewModel.RegisterKioskWindow(handle);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _blueLightFilter.StartMonitoringDisplayChanges();
        _viewModel.PrepareSession();
        if (!_viewModel.EnsureSubDisplayName(this))
        {
            _forceClose = true;
            Close();
            return;
        }

        _blueLightFilter.SetActive(_viewModel.IsBlueLightFilterActive, _viewModel.BlueLightFilterPhase);
        ShowGuardiWidget();
        _ = _viewModel.FinishInitializeAsync();
    }

    public void RestoreFromWidget() => RestoreMainWindow();

    private void RestoreMainWindow()
    {
        if (_viewModel.IsKioskActive)
            EnsureKioskBounds();
        else if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        if (!_viewModel.IsKioskActive)
            ShowInTaskbar = true;

        Show();
        Activate();
        Focus();
        _guardiWidget?.SendToDesktopLayer();
    }

    private bool CanHideToTray() =>
        !_viewModel.IsKioskActive && (_viewModel.IsEnrolled || _viewModel.IsLocalMode);

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
            return;

        _trayIcon = new GuardiTrayIconService(
            onOpen: () => Dispatcher.BeginInvoke(RestoreMainWindow),
            onQuit: () => Dispatcher.BeginInvoke(ShowCloseConfirmDialog));
    }

    private void HideToTray()
    {
        if (!CanHideToTray())
            return;

        EnsureTrayIcon();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        ShowInTaskbar = false;
        Hide();
        _guardiWidget?.Show();

        AuditLog.Write("Main window hidden to notification area — Guardi keeps running.");

        if (_trayBalloonShown || _trayIcon is null)
            return;

        _trayBalloonShown = true;
        _trayIcon.ShowBalloon(UiCopy.TrayHideBalloonTitle, UiCopy.TrayHideBalloonMessage);
    }

    private void ShowGuardiWidget()
    {
        if (_guardiWidget is not null)
            return;

        _guardiWidget = new GuardiWidgetWindow(this) { DataContext = _viewModel };
        _guardiWidget.Show();
    }

    private void OnActivated(object? sender, EventArgs e) =>
        _guardiWidget?.SendToDesktopLayer();

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsKioskActive)
            return;

        if (e.ClickCount == 2)
            return;

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsKioskActive)
            return;

        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        RequestClose();

    private void RequestClose()
    {
        if (_isQuitting)
            return;

        if (CanHideToTray())
        {
            HideToTray();
            return;
        }

        if (!_viewModel.IsEnrolled)
        {
            if (!_viewModel.TryAuthorizeQuit())
                return;

            _ = FinalizeQuitAsync();
        }
    }

    private void ShowCloseConfirmDialog()
    {
        if (_closeDialogOpen || _forceClose || _isQuitting)
            return;

        _closeDialogOpen = true;
        try
        {
            var dialog = new CloseAppConfirmOverlayWindow(_viewModel.ExitPinRequired) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (!_viewModel.TryAuthorizeQuit())
                return;

            _ = FinalizeQuitAsync();
        }
        finally
        {
            _closeDialogOpen = false;
        }
    }

    private async Task FinalizeQuitAsync()
    {
        if (_isQuitting)
            return;

        _isQuitting = true;
        _forceClose = true;

        // Tell the mutual watchdog before teardown so it does not resurrect this process.
        ProcessGuardian.SignalIntentionalShutdown();

        await Dispatcher.InvokeAsync(() =>
        {
            CloseChildWindows();
            Hide();
        }, DispatcherPriority.Send);

        try
        {
            await Task.Run(() => _viewModel.ShutdownCore())
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort shutdown — still close the app.
        }

        await Dispatcher.InvokeAsync(() =>
        {
            Close();
            Application.Current.Shutdown();
        }, DispatcherPriority.Send);
    }

    private void CloseChildWindows()
    {
        _blueLightFilter.SetActive(false);
        _trayIcon?.Dispose();
        _trayIcon = null;
        _guardiWidget?.Close();
        _guardiWidget = null;
        HideLockOverlayIfNeeded();
        _domMessageOverlay?.Close();
        _domMessageOverlay = null;
        _appBlockedToast?.Close();
        _appBlockedToast = null;
        _bedtimeWarningToast?.Close();
        _bedtimeWarningToast = null;
        _gamingHud?.Close();
        _gamingHud = null;
        _youtubeHud?.Close();
        _youtubeHud = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopKioskShellWatch();
        DisableKioskWindowHook();
        CloseChildWindows();
        _viewModel.ScreenTimeLocked -= OnLockOverlayRequested;
        _viewModel.KioskStateChanged -= OnKioskStateChanged;
        _viewModel.ScreenTimeLockDismissed -= OnLockOverlayDismissed;
        _viewModel.LockOverlayChanged -= OnLockOverlayChanged;
        _viewModel.DomMessagePopupRequested -= OnDomMessagePopupRequested;
        _viewModel.AppBlockedPopupRequested -= OnAppBlockedPopupRequested;
        _viewModel.BedtimeWarningPopupRequested -= OnBedtimeWarningPopupRequested;
        _viewModel.StudyToastPopupRequested -= OnStudyToastPopupRequested;
        _viewModel.BlueLightFilterStateChanged -= OnBlueLightFilterStateChanged;
        _blueLightFilter.Dispose();
        _viewModel.GamingHudStateChanged -= OnGamingHudStateChanged;
        _viewModel.GamingSessionToastRequested -= OnGamingSessionToastRequested;
        _viewModel.YoutubeHudStateChanged -= OnYoutubeHudStateChanged;
        _viewModel.ExtensionGuardStateChanged -= OnExtensionGuardStateChanged;
        _viewModel.FirefoxRestartToastRequested -= OnFirefoxRestartToastRequested;
        _viewModel.BrowserRestartCountdownRequested -= OnBrowserRestartCountdownRequested;
        _viewModel.Dispose();
    }

    private void OnLockOverlayRequested() => ShowLockOverlayIfNeeded();

    private void OnLockOverlayDismissed() => HideLockOverlayIfNeeded();

    private void OnLockOverlayChanged() => UpdateLockOverlay();

    private void UpdateLockOverlay()
    {
        if (_viewModel.IsLockOverlayVisible)
            ShowLockOverlayIfNeeded();
        else
            HideLockOverlayIfNeeded();
    }

    private void ShowLockOverlayIfNeeded()
    {
        if (_lockOverlays.Count > 0)
            return;

        var overlay = new LockOverlayWindow
        {
            DataContext = _viewModel,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            WindowState = WindowState.Normal
        };
        
        _lockOverlays.Add(overlay);
        overlay.Show();
    }

    private void HideLockOverlayIfNeeded()
    {
        foreach (var overlay in _lockOverlays)
        {
            overlay.ForceClose();
        }
        _lockOverlays.Clear();
    }

    private void OnDomMessagePopupRequested(string message) =>
        ShowGuardiMessageOverlay(UiCopy.DomMessageTitle, message);

    private void OnAppBlockedPopupRequested(string title, string message)
    {
        _appBlockedToast?.Close();

        _appBlockedToast = new AppBlockedToastWindow(title, message);
        _appBlockedToast.Closed += (_, _) => _appBlockedToast = null;
        _appBlockedToast.Show();
    }

    private void OnBedtimeWarningPopupRequested(string title, string message)
    {
        _bedtimeWarningToast?.Close();

        _bedtimeWarningToast = new BedtimeWarningToastWindow(title, message);
        _bedtimeWarningToast.Closed += (_, _) => _bedtimeWarningToast = null;
        _bedtimeWarningToast.Show();
    }

    private void OnStudyToastPopupRequested(string title, string message)
    {
        _bedtimeWarningToast?.Close();

        _bedtimeWarningToast = new BedtimeWarningToastWindow(title, message, UiCopy.IconStudy);
        _bedtimeWarningToast.Closed += (_, _) => _bedtimeWarningToast = null;
        _bedtimeWarningToast.Show();
    }

    private void OnBlueLightFilterStateChanged(bool active, BlueLightFilterPhase phase) =>
        _blueLightFilter.SetActive(active, phase);

    private void OnGamingSessionToastRequested(GamingSessionToast toast)
    {
        _gamingSessionToast?.Close();

        _gamingSessionToast = new GamingSessionToastWindow(toast);
        _gamingSessionToast.Closed += (_, _) => _gamingSessionToast = null;
        _gamingSessionToast.Show();
    }

    private void OnGamingHudStateChanged(GamingHudState? state)
    {
        if (state is null)
        {
            _gamingHud?.Close();
            _gamingHud = null;
            return;
        }

        if (_gamingHud is null)
        {
            _gamingHud = new GamingHudOverlayWindow();
            _gamingHud.Closed += (_, _) => _gamingHud = null;
            _gamingHud.Show();
        }

        _gamingHud.Update(state);
    }

    private void OnYoutubeHudStateChanged(YoutubeHudState? state)
    {
        if (state is null)
        {
            _youtubeHud?.Close();
            _youtubeHud = null;
            return;
        }

        if (_youtubeHud is null)
        {
            _youtubeHud = new YoutubeHudOverlayWindow();
            _youtubeHud.Closed += (_, _) => _youtubeHud = null;
            _youtubeHud.Show();
        }

        _youtubeHud.Update(state);
    }

    private void OnFirefoxRestartToastRequested(string title, string message)
    {
        ShowTransientToast(title, message, TimeSpan.FromSeconds(4));
    }

    private void OnBrowserRestartCountdownRequested(string browser, string message, int secondsRemaining)
    {
        if (secondsRemaining <= 0)
        {
            _appBlockedToast?.Close();
            _appBlockedToast = null;
            return;
        }

        var title = ExtensionGuardCopy.SoftRestartCountdownTitle(browser);
        if (_appBlockedToast is null)
        {
            _appBlockedToast = new AppBlockedToastWindow(title, message);
            _appBlockedToast.SetPersistent(true);
            _appBlockedToast.Closed += (_, _) => _appBlockedToast = null;
            _appBlockedToast.Show();
            return;
        }

        _appBlockedToast.UpdateContent(title, message);
    }

    private void ShowExtensionGuardToast(ExtensionGuardState state) =>
        ShowTransientToast(state.Headline, state.Body, TimeSpan.FromSeconds(6));

    private void ShowTransientToast(string title, string message, TimeSpan duration)
    {
        _appBlockedToast?.Close();

        _appBlockedToast = new AppBlockedToastWindow(title, message, duration);
        _appBlockedToast.Closed += (_, _) => _appBlockedToast = null;
        _appBlockedToast.Show();
    }

    private void OnExtensionGuardStateChanged(ExtensionGuardState? state)
    {
        if (state is null)
        {
            _extensionOverlay?.ForceClose();
            _extensionOverlay = null;
            return;
        }

        if (!Config.ExtensionGuardShowInstallingOverlay)
        {
            _extensionOverlay?.ForceClose();
            _extensionOverlay = null;
            ShowExtensionGuardToast(state);
            return;
        }

        if (_extensionOverlay is null)
        {
            Action? devSkip = Config.ExtensionGuardDevBypass
                ? _viewModel.DismissExtensionGuardOverlay
                : null;

            _extensionOverlay = new ExtensionInstallOverlayWindow(state, devSkip)
            {
                Owner = this,
            };
            _extensionOverlay.Closed += (_, _) => _extensionOverlay = null;
            _extensionOverlay.Show();
        }
        else
        {
            _extensionOverlay.Update(state);
        }
    }

    private void OnModePresentationChanged()
    {
        ApplyWindowChrome();
        UpdateWindowRoundness();
        _guardiWidget?.ApplyPresentation();
    }

    private void OnKioskStateChanged(bool active)
    {
        try
        {
            if (active)
                EnterKioskPresentation();
            else
                ExitKioskPresentation();

            UpdateWindowRoundness();
        }
        catch (Exception ex)
        {
            AuditLog.Write($"OnKioskStateChanged failed: {ex}");
            _viewModel.EmergencyReleaseKiosk();
        }
    }

    private void EnterKioskPresentation()
    {
        _preKioskWindowState = WindowState;
        _preKioskResizeMode = ResizeMode;
        _preKioskShowInTaskbar = ShowInTaskbar;
        _preKioskLeft = Left;
        _preKioskTop = Top;
        _preKioskWidth = Width;
        _preKioskHeight = Height;
        _guardiWidget?.Hide();

        _kioskCurtain?.ForceClose();
        _kioskCurtain = null;

        Show();
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        EnsureKioskBounds();
        EnsureDashboardVisible();

        Dispatcher.BeginInvoke(() =>
        {
            RegisterKioskHandle();
            EnableKioskWindowHook();
            Activate();
            Focus();
        }, DispatcherPriority.Loaded);

        StartKioskShellWatch();
    }

    private void ExitKioskPresentation()
    {
        StopKioskShellWatch();
        DisableKioskWindowHook();
        WindowState = _preKioskWindowState;
        ResizeMode = _preKioskResizeMode;
        ShowInTaskbar = _preKioskShowInTaskbar;
        Left = _preKioskLeft;
        Top = _preKioskTop;
        Width = _preKioskWidth;
        Height = _preKioskHeight;
        UpdateWindowRoundness();

        _guardiWidget?.Show();
        Show();
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (_viewModel.IsKioskActive)
        {
            if (WindowState == WindowState.Minimized)
                EnsureKioskBounds();
            return;
        }

        UpdateWindowRoundness();
    }

    private void StartKioskShellWatch()
    {
        _kioskShellWatchTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(Config.KioskShellWatchIntervalMs),
            DispatcherPriority.Background,
            (_, _) => EnforceKioskShell(),
            Dispatcher);

        EnforceKioskShell();
        _kioskShellWatchTimer.Start();
    }

    private void StopKioskShellWatch() => _kioskShellWatchTimer?.Stop();

    private void EnforceKioskShell()
    {
        if (!_viewModel.IsKioskActive)
            return;

        if (!IsVisible)
            Show();

        EnsureKioskBounds();
    }

    private void EnableKioskWindowHook()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        _kioskHwndSource = HwndSource.FromHwnd(handle);
        _kioskHwndSource?.AddHook(KioskWndProc);
    }

    private void DisableKioskWindowHook()
    {
        if (_kioskHwndSource is null)
            return;

        _kioskHwndSource.RemoveHook(KioskWndProc);
        _kioskHwndSource = null;
    }

    private const int WmSysCommand = 0x0112;
    private const int ScMinimize = 0xF020;
    private const int ScMove = 0xF010;
    private const int ScSize = 0xF000;

    private IntPtr KioskWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_viewModel.IsKioskActive || msg != WmSysCommand)
            return IntPtr.Zero;

        var command = wParam.ToInt32() & 0xFFF0;
        if (command is ScMinimize or ScMove or ScSize)
        {
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void ShowGuardiMessageOverlay(string title, string message)
    {
        _domMessageOverlay?.Close();

        _domMessageOverlay = new DomMessageOverlayWindow(message, title);
        _domMessageOverlay.Closed += (_, _) => _domMessageOverlay = null;
        _domMessageOverlay.Show();
        _domMessageOverlay.Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_forceClose || _isQuitting)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (_closeDialogOpen)
            return;

        if (CanHideToTray())
        {
            HideToTray();
            return;
        }

        if (!_viewModel.IsEnrolled)
        {
            if (!_viewModel.TryAuthorizeQuit())
                return;

            _ = FinalizeQuitAsync();
        }
    }
}

internal sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
