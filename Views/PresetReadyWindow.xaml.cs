using System.Runtime.Versioning;
using System.Windows;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
internal partial class PresetReadyWindow : Window
{
    public PresetReadyWindow(string subName)
    {
        InitializeComponent();
        HeadlineText.Text = $"Hey {subName} — you're all set!";
        BodyText.Text = "Guardi is already configured and watching over this computer. " +
                        "Screen time, bedtime, and all the rules are in place.\n\n" +
                        "Just use the computer normally — Guardi takes care of the rest.";
    }

    private void OnGotItClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
