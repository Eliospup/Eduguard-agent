using System.Windows;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Views;

public partial class DomMessageOverlayWindow : Window
{
    public DomMessageOverlayWindow(string message, string? title = null)
    {
        InitializeComponent();
        TitleText.Text = title ?? UiCopy.DomMessageTitle;
        MessageText.Text = message;
        DismissButton.Content = UiCopy.DomMessageDismissButton;
        Mascot.Visibility = UiPresentationState.Current.ShowMascot
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    private void OnDismissClick(object sender, RoutedEventArgs e) => Close();
}
