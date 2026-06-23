using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Views;

internal partial class ExitPinPromptWindow : Window
{
    private readonly ExitPinService _exitPin;
    private readonly string _context;
    private readonly DispatcherTimer _lockoutTimer;

    public ExitPinPromptWindow(ExitPinService exitPin, string context)
    {
        InitializeComponent();
        _exitPin = exitPin;
        _context = context;

        TitleText.Text = UiCopy.ExitPinTitle;
        PromptText.Text = string.Equals(context, "unlink", StringComparison.OrdinalIgnoreCase)
            ? UiCopy.ExitPinUnlinkPrompt
            : UiCopy.ExitPinPrompt;
        ConfirmButton.Content = UiCopy.ExitPinConfirm;
        CancelButton.Content = UiCopy.ExitPinCancel;
        Mascot.Visibility = UiPresentationState.Current.ShowMascot
            ? Visibility.Visible
            : Visibility.Collapsed;

        _lockoutTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnLockoutTick, Dispatcher);
        Loaded += (_, _) =>
        {
            PinInput.PreviewTextInput += OnPinPreviewTextInput;
            DataObject.AddPastingHandler(PinInput, OnPinPaste);
            UpdateLockoutUi();
            PinInput.Focus();
        };
        Closed += (_, _) => _lockoutTimer.Stop();
    }

    private static void OnPinPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]$");

    private static void OnPinPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (!Regex.IsMatch(text, @"^[0-9]{1,8}$"))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => SubmitPin();

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SubmitPin();
    }

    private void SubmitPin()
    {
        if (_exitPin.IsLockedOut(out _))
        {
            UpdateLockoutUi();
            return;
        }

        var attempt = PinInput.Password;
        if (attempt.Length < 4)
        {
            ShowError(UiCopy.ExitPinWrong);
            return;
        }

        if (_exitPin.TryVerify(attempt, _context))
        {
            DialogResult = true;
            Close();
            return;
        }

        PinInput.Clear();
        PinInput.Focus();

        if (_exitPin.IsLockedOut(out _))
            UpdateLockoutUi();
        else
            ShowError(UiCopy.ExitPinWrong);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void UpdateLockoutUi()
    {
        if (_exitPin.IsLockedOut(out var remaining))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            ErrorText.Text = string.Format(UiCopy.ExitPinLockout, seconds);
            ErrorText.Visibility = Visibility.Visible;
            PinInput.IsEnabled = false;
            ConfirmButton.IsEnabled = false;
            if (!_lockoutTimer.IsEnabled)
                _lockoutTimer.Start();
            return;
        }

        _lockoutTimer.Stop();
        PinInput.IsEnabled = true;
        ConfirmButton.IsEnabled = true;
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void OnLockoutTick(object? sender, EventArgs e)
    {
        if (_exitPin.IsLockedOut(out var remaining))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            ErrorText.Text = string.Format(UiCopy.ExitPinLockout, seconds);
            return;
        }

        UpdateLockoutUi();
        PinInput.Focus();
    }
}
