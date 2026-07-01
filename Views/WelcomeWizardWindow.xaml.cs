using System.Windows;
using System.Windows.Input;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.Views;

/// <summary>
/// First-run setup: local-vs-online choice, Sub name, and (local only) exit PIN. Always
/// rendered in the Trusted Sub theme regardless of any prior mode state — onboarding
/// happens before any supervision mode is meaningfully chosen, so there is no "current
/// mode" to reflect yet.
/// </summary>
internal partial class WelcomeWizardWindow : Window
{
    private readonly SubProfileService _subProfile;
    private readonly ExitPinService _exitPin;
    private bool _choseLocal;

    /// <summary>True if the Sub chose "On this PC" — the caller enables local mode accordingly.</summary>
    public bool ChoseLocalSetup => _choseLocal;

    public WelcomeWizardWindow(SubProfileService subProfile, ExitPinService exitPin)
    {
        // Must run before InitializeComponent — StaticResource brushes in the XAML resolve
        // against whatever is in the resource dictionary at parse time.
        ThemeService.Apply(AgentModeRegistry.TrustedSub.Theme, AgentModeRegistry.TrustedSub.Ui);

        InitializeComponent();
        _subProfile = subProfile;
        _exitPin = exitPin;

        ModeTitleText.Text = UiCopy.WelcomeModeTitle;
        ModeSubtitleText.Text = UiCopy.WelcomeModeSubtitle;
        LocalChoiceTitleText.Text = UiCopy.WelcomeModeLocalTitle;
        LocalChoiceBodyText.Text = UiCopy.WelcomeModeLocalBody;
        OnlineChoiceTitleText.Text = UiCopy.WelcomeModeOnlineTitle;
        OnlineChoiceBodyText.Text = UiCopy.WelcomeModeOnlineBody;
        OnlineBadgeText.Text = UiCopy.WelcomeModeOnlineBadge;

        NameTitleText.Text = UiCopy.SubNamePromptTitle;
        NamePromptText.Text = UiCopy.SubNamePromptBody;
        NameConfirmButton.Content = UiCopy.SubNamePromptConfirm;

        PinTitleText.Text = UiCopy.WelcomePinTitle;
        PinPromptText.Text = UiCopy.WelcomePinBody;
        PinConfirmButton.Content = UiCopy.WelcomePinConfirm;
        PinSkipButton.Content = UiCopy.WelcomePinSkip;

        Loaded += (_, _) => LocalChoiceButton.Focus();
    }

    private void OnChooseLocalClick(object sender, RoutedEventArgs e)
    {
        _choseLocal = true;
        ModeStep.Visibility = Visibility.Collapsed;
        NameStep.Visibility = Visibility.Visible;
        NameInput.Focus();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SubmitName();
    }

    private void OnNameConfirmClick(object sender, RoutedEventArgs e) => SubmitName();

    private void SubmitName()
    {
        if (!_subProfile.TrySetDisplayName(NameInput.Text))
        {
            ShowError(NameErrorText, UiCopy.SubNamePromptInvalid);
            return;
        }

        if (!_choseLocal)
        {
            // Online setup isn't implemented yet — unreachable today since that choice is
            // disabled, but finish cleanly rather than fall into the PIN step if it ever is.
            DialogResult = true;
            Close();
            return;
        }

        NameStep.Visibility = Visibility.Collapsed;
        PinStep.Visibility = Visibility.Visible;
        PinInput.Focus();
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SubmitPin();
    }

    private void OnPinConfirmClick(object sender, RoutedEventArgs e) => SubmitPin();

    private void OnPinSkipClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SubmitPin()
    {
        var pin = PinInput.Password;
        if (!ExitPinService.IsValidFormat(pin))
        {
            ShowError(PinErrorText, UiCopy.WelcomePinInvalid);
            return;
        }

        _exitPin.SetPin(pin);
        DialogResult = true;
        Close();
    }

    private static void ShowError(System.Windows.Controls.TextBlock target, string message)
    {
        target.Text = message;
        target.Visibility = Visibility.Visible;
    }
}
