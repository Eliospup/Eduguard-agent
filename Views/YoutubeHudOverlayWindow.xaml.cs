using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class YoutubeHudOverlayWindow : Window
{
    private static readonly Brush NeonRed = (Brush)Application.Current.FindResource("NeonRedBrush");
    private static readonly Brush NormalFg = (Brush)Application.Current.FindResource("PrimaryBrush");

    public YoutubeHudOverlayWindow()
    {
        InitializeComponent();
        TitleText.Text = UiCopy.YoutubeHudTitle;
    }

    public void Update(YoutubeHudState state)
    {
        SourceText.Text = state.SourceLabel;
        CountdownText.Text = state.RemainingLabel;
        ProgressBar.Value = state.Progress;
        ApplyInfractionStyle(state.Exhausted);
    }

    private void ApplyInfractionStyle(bool exhausted)
    {
        CountdownText.Foreground = exhausted ? NeonRed : NormalFg;
        ProgressBar.Foreground = exhausted ? NeonRed : NormalFg;
        HudBorder.Effect = exhausted
            ? new DropShadowEffect { Color = Colors.Red, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 }
            : new DropShadowEffect { Color = Color.FromRgb(0x03, 0x69, 0xA1), BlurRadius = 10, ShadowDepth = 2, Opacity = 0.22 };
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionTopLeft();
    }

    private void PositionTopLeft()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + 12;
        Top = area.Top + 12;
    }
}
