using System;
using System.Globalization;
using System.Windows;
using EduGuardAgent.Profiles;
using EduGuardAgent.Services;

namespace EduGuardAgent.Views;

internal partial class SelfLockMessageWindow : Window
{
    public SelfLockMessageWindow(TimeSpan remaining, DateTimeOffset until)
    {
        InitializeComponent();

        UiPresentationState.ApplyMascotVisibility(Mascot, TrustedSubMascotIcon, RestrictedSubMascotIcon);

        TitleText.Text = UiCopy.SelfLockTitle;
        BodyText.Text = UiCopy.SelfLockBody;
        RemainingText.Text = string.Format(
            CultureInfo.CurrentCulture,
            UiCopy.SelfLockRemainingFormat,
            SelfLockService.Describe(remaining),
            until.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture));
        DismissButton.Content = UiCopy.SelfLockDismiss;
    }

    private void OnDismissClick(object sender, RoutedEventArgs e) => Close();
}
