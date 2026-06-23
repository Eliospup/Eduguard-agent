using System.Windows;
using System.Windows.Input;
using EduGuardAgent.Profiles;
using EduGuardAgent.Services;

namespace EduGuardAgent.Views;

internal partial class SubNamePromptWindow : Window
{
    private readonly SubProfileService _subProfile;

    public SubNamePromptWindow(SubProfileService subProfile)
    {
        InitializeComponent();
        _subProfile = subProfile;

        TitleText.Text = UiCopy.SubNamePromptTitle;
        PromptText.Text = UiCopy.SubNamePromptBody;
        ConfirmButton.Content = UiCopy.SubNamePromptConfirm;
        Mascot.Visibility = UiPresentationState.Current.ShowMascot
            ? Visibility.Visible
            : Visibility.Collapsed;

        Loaded += (_, _) => NameInput.Focus();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => SubmitName();

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SubmitName();
    }

    private void SubmitName()
    {
        if (!_subProfile.TrySetDisplayName(NameInput.Text))
        {
            ShowError(UiCopy.SubNamePromptInvalid);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
