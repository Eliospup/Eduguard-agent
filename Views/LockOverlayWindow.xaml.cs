using System.ComponentModel;
using System.Windows;

namespace EduGuardAgent.Views;

public partial class LockOverlayWindow : Window
{
    private bool _isForceClosing;

    public LockOverlayWindow()
    {
        InitializeComponent();
    }

    public void ForceClose()
    {
        _isForceClosing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isForceClosing)
        {
            e.Cancel = true;
        }
        else
        {
            base.OnClosing(e);
        }
    }
}
