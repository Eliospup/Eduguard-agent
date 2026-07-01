using System.Windows;

namespace EduGuardAgent.Views;

public partial class GamingSoftLimitOverlayWindow : Window
{
    public bool UserChoseToContinue { get; private set; }

    public GamingSoftLimitOverlayWindow(string limitLabel)
    {
        InitializeComponent();
        TitleText.Text = Profiles.UiCopy.GamingSoftLimitTitle;
        MessageText.Text = Profiles.UiCopy.GamingSoftLimitMessage(limitLabel);
        HintText.Text = Profiles.UiCopy.GamingSoftLimitHint;
        StopButton.Content = Profiles.UiCopy.GamingSoftLimitStopButton;
        ContinueButton.Content = Profiles.UiCopy.GamingSoftLimitContinueButton;
        Profiles.UiPresentationState.ApplyMascotVisibility(Mascot, TrustedSubMascotIcon, RestrictedSubMascotIcon);
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        UserChoseToContinue = false;
        Close();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        UserChoseToContinue = true;
        Close();
    }
}
