using System.ComponentModel;
using System.Windows;

namespace EduGuardAgent.Views;

/// <summary>
/// Soft fullscreen warning shown in Trusted Sub mode when the global screen-time limit is hit.
/// Unlike <see cref="LockOverlayWindow"/> it isn't a real block: the Sub dismisses it themselves
/// via the discouraged "continue anyway" button, no Dom PIN required.
/// </summary>
public partial class SoftLimitWarningWindow : Window
{
    private bool _isForceClosing;

    public SoftLimitWarningWindow()
    {
        InitializeComponent();
    }

    public void ForceClose()
    {
        _isForceClosing = true;
        Close();
    }

    private void OnContinueClick(object sender, RoutedEventArgs e) => ForceClose();

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_isForceClosing)
            e.Cancel = true;
    }
}
