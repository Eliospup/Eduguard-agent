using System.Runtime.Versioning;
using System.Windows;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class YoutubeHudOverlayWindow : Window
{
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
