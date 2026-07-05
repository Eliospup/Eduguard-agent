using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using EduGuardAgent.Models;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class GamingSessionToastWindow : Window
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(6);
    private DispatcherTimer? _hideTimer;

    public GamingSessionToastWindow(GamingSessionToast toast)
    {
        InitializeComponent();
        IconText.Text = toast.IconGlyph;
        TitleText.Text = toast.Title;
        MessageText.Text = toast.Message;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionBottomRight();
        StartHideTimer();
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 24;
        Top = area.Bottom - ActualHeight - 24;
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
