using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class BedtimeWarningToastWindow : Window
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(6);
    private DispatcherTimer? _hideTimer;

    public BedtimeWarningToastWindow(string title, string message, string? iconGlyph = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        if (!string.IsNullOrWhiteSpace(iconGlyph))
        {
            MoonIcon.Visibility = Visibility.Collapsed;
            IconText.Text = iconGlyph;
            IconText.Visibility = Visibility.Visible;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionCenterScreen();
        StartHideTimer();
    }

    private void PositionCenterScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    }

    private void StartHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = VisibleDuration,
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            Close();
        };
        _hideTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer?.Stop();
        _hideTimer = null;
        base.OnClosed(e);
    }
}
