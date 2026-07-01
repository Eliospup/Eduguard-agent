using System.Windows;

namespace EduGuardAgent.Views;

public partial class CloseAppConfirmOverlayWindow : Window
{
    public CloseAppConfirmOverlayWindow(bool exitPinRequired = false)
    {
        InitializeComponent();
        TitleText.Text = Profiles.UiCopy.CloseAppTitle;
        MessageText.Text = Profiles.UiCopy.CloseAppWarning;
        HintText.Visibility = exitPinRequired ? Visibility.Collapsed : Visibility.Visible;
        if (!exitPinRequired)
            HintText.Text = Profiles.UiCopy.TrayQuitHint;
        StayButton.Content = Profiles.UiCopy.CloseAppStayButton;
        QuitButton.Content = Profiles.UiCopy.CloseAppQuitButton;
        Profiles.UiPresentationState.ApplyMascotVisibility(Mascot, TrustedSubMascotIcon, RestrictedSubMascotIcon);
    }
    private void OnStayClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
