using System.Runtime.Versioning;
using System.Windows;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class GamingHudOverlayWindow : Window
{
    public GamingHudOverlayWindow()
    {
        InitializeComponent();
        TitleText.Text = UiCopy.GamingHudTitle;
    }

    public void Update(GamingHudState state)
    {
        GameNameText.Text = state.GameName;
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
