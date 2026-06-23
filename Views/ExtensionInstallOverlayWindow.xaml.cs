using System.ComponentModel;
using System.Windows;
using EduGuardAgent.Models;

namespace EduGuardAgent.Views;

public partial class ExtensionInstallOverlayWindow : Window
{
    private readonly Action? _onDevSkip;
    private bool _allowClose;

    internal ExtensionInstallOverlayWindow(
        ExtensionGuardState state,
        Action? onDevSkip = null)
    {
        InitializeComponent();
        _onDevSkip = onDevSkip;
        DevSkipButton.Visibility = onDevSkip is not null ? Visibility.Visible : Visibility.Collapsed;
        Update(state);
    }

    internal void Update(ExtensionGuardState state)
    {
        HeadlineText.Text = state.Headline;
        BodyText.Text = state.Body;
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnDevSkipClicked(object sender, RoutedEventArgs e) => _onDevSkip?.Invoke();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
