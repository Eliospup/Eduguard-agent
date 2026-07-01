using EduGuardAgent.Profiles;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class AppBlockedToastWindow : Window
{
    private static readonly TimeSpan DefaultVisibleDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FirefoxReleaseVisibleDuration = TimeSpan.FromSeconds(4.5);

    private readonly TimeSpan _visibleDuration;
    private DispatcherTimer? _hideTimer;
    private bool _autoHide = true;

    public AppBlockedToastWindow(string title, string message, TimeSpan? visibleDuration = null)
    {
        _visibleDuration = visibleDuration
            ?? (string.Equals(title, ExtensionGuardCopy.FirefoxReleaseBlockedTitle, StringComparison.Ordinal)
                ? FirefoxReleaseVisibleDuration
                : DefaultVisibleDuration);
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        UiPresentationState.ApplyMascotVisibility(Mascot, TrustedSubMascotIcon, RestrictedSubMascotIcon);
    }

    public void SetPersistent(bool persistent)
    {
        _autoHide = !persistent;
        if (persistent)
            _hideTimer?.Stop();
        else
            StartHideTimer();
    }

    public void UpdateContent(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        PositionBottomCenter();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionBottomCenter();
        StartHideTimer();
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 24;
    }

    private void StartHideTimer()
    {
        if (!_autoHide)
            return;

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = _visibleDuration,
        };
        _hideTimer.Tick += OnHideTimerTick;
        _hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer?.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer?.Stop();
        _hideTimer = null;
        base.OnClosed(e);
    }
}
