using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class GuardiTrayMenuWindow : Window
{
    private static GuardiTrayMenuWindow? _current;
    private readonly Action _onOpen;
    private readonly Action _onQuit;
    private bool _actionTaken;

    private GuardiTrayMenuWindow(Action onOpen, Action onQuit)
    {
        InitializeComponent();
        _onOpen = onOpen;
        _onQuit = onQuit;

        TitleText.Text = UiCopy.TrayMenuTitle;
        SubtitleText.Text = UiCopy.TrayMenuSubtitle;
        OpenLabel.Text = UiCopy.TrayOpenMenuItem;
        QuitLabel.Text = UiCopy.TrayQuitMenuItem;

        var presentation = UiPresentationState.Current;
        Mascot.Visibility = presentation.ShowMascot ? Visibility.Visible : Visibility.Collapsed;
        ShieldHost.Visibility = presentation.ShowMascot ? Visibility.Collapsed : Visibility.Visible;
    }

    public static void ShowNear(int screenX, int screenY, Action onOpen, Action onQuit)
    {
        if (Application.Current?.Dispatcher is null)
            return;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _current?.Close();
            var menu = new GuardiTrayMenuWindow(onOpen, onQuit);
            _current = menu;
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_current, menu))
                    _current = null;
            };
            menu.ShowAt(screenX, screenY);
        });
    }

    private void ShowAt(int screenX, int screenY)
    {
        Show();
        UpdateLayout();

        var source = PresentationSource.FromVisual(this) as HwndSource;
        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        var left = screenX / dpiX - width + 8;
        var top = screenY / dpiY - height - 8;

        var workArea = SystemParameters.WorkArea;
        left = Math.Max(workArea.Left + 8, Math.Min(left, workArea.Right - width - 8));
        top = Math.Max(workArea.Top + 8, Math.Min(top, workArea.Bottom - height - 8));

        Left = left;
        Top = top;
        Activate();
        Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
            SetForegroundWindow(hwnd);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_actionTaken)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (!_actionTaken && IsVisible)
                Close();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
            Close();
        base.OnKeyDown(e);
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        _actionTaken = true;
        Close();
        _onOpen();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        _actionTaken = true;
        Close();
        _onQuit();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
