using System;
using System.Globalization;
using System.Windows;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Views;

internal partial class SelfLockConfirmWindow : Window
{
    public SelfLockConfirmWindow(string durationText, DateTimeOffset unlockAt)
    {
        InitializeComponent();

        UiPresentationState.ApplyMascotVisibility(Mascot, TrustedSubMascotIcon, RestrictedSubMascotIcon);

        TitleText.Text = UiCopy.SelfLockConfirmTitle;
        MessageText.Text = string.Format(
            CultureInfo.CurrentCulture,
            UiCopy.SelfLockConfirmBodyFormat,
            unlockAt.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture),
            durationText);
        WarningText.Text = UiCopy.SelfLockConfirmWarning;
        CancelButton.Content = UiCopy.SelfLockConfirmCancel;
        ConfirmButton.Content = UiCopy.SelfLockConfirmYes;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
