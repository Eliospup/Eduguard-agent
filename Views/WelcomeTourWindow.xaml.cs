using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Services;

namespace EduGuardAgent.Views;

/// <summary>
/// Mandatory, one-time tour shown right after first-run onboarding completes: an intro,
/// one dedicated page per mode (themed and mascotted like that mode, for a real preview),
/// and a page explaining the trust meter. No skip path — closing is cancelled unless the
/// Sub reaches the final button on the last page.
/// </summary>
internal partial class WelcomeTourWindow : Window
{
    private enum MascotKind { Guardi, TrustedSub, RestrictedSub }

    private sealed record TourPage(
        string Title,
        string? Kicker,
        string Body,
        ModeTheme Theme,
        ModeUiPresentation Ui,
        MascotKind Mascot,
        bool ShowTrustBar);

    private readonly TourPage[] _pages;
    private readonly Ellipse[] _dots;
    private int _pageIndex;
    private bool _completed;

    public WelcomeTourWindow()
    {
        InitializeComponent();

        _pages =
        [
            new TourPage(UiCopy.TourWhyTitle, null, UiCopy.TourWhyBody,
                AgentModeRegistry.TrustedSub.Theme, AgentModeRegistry.TrustedSub.Ui, MascotKind.Guardi, false),
            new TourPage(UiCopy.TourTrustedSubTitle, UiCopy.TourTrustedSubKicker, UiCopy.TourTrustedSubBody,
                AgentModeRegistry.TrustedSub.Theme, AgentModeRegistry.TrustedSub.Ui, MascotKind.TrustedSub, false),
            new TourPage(UiCopy.TourSubTitle, UiCopy.TourSubKicker, UiCopy.TourSubBody,
                AgentModeRegistry.Sub.Theme, AgentModeRegistry.Sub.Ui, MascotKind.Guardi, false),
            new TourPage(UiCopy.TourRestrictedSubTitle, UiCopy.TourRestrictedSubKicker, UiCopy.TourRestrictedSubBody,
                AgentModeRegistry.RestrictedSub.Theme, AgentModeRegistry.RestrictedSub.Ui, MascotKind.RestrictedSub, false),
            new TourPage(UiCopy.TourTrustTitle, null, UiCopy.TourTrustBody,
                AgentModeRegistry.TrustedSub.Theme, AgentModeRegistry.TrustedSub.Ui, MascotKind.Guardi, true),
        ];

        _dots = [Dot0, Dot1, Dot2, Dot3, Dot4];

        TrustFullLabelText.Text = UiCopy.TourTrustFullLabel;
        TrustLowLabelText.Text = UiCopy.TourTrustLowLabel;
        BackButton.Content = UiCopy.TourBack;

        RenderPage();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_pageIndex >= _pages.Length - 1)
        {
            _completed = true;
            Close();
            return;
        }

        _pageIndex++;
        RenderPage();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_pageIndex == 0)
            return;

        _pageIndex--;
        RenderPage();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        // Mandatory tour: block Alt+F4 / task-close until the Sub has actually reached
        // and clicked through the final page.
        if (!_completed)
            e.Cancel = true;
    }

    private void RenderPage()
    {
        var page = _pages[_pageIndex];

        // Re-applies the app-wide DynamicResource theme brushes this window's XAML binds
        // to, so each mode page shows that mode's real colors — a genuine preview, not just
        // a palette-swapped label.
        ThemeService.Apply(page.Theme, page.Ui);

        KickerText.Text = page.Kicker ?? string.Empty;
        KickerText.Visibility = page.Kicker is null ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = page.Title;
        BodyText.Text = page.Body;
        TrustBarPanel.Visibility = page.ShowTrustBar ? Visibility.Visible : Visibility.Collapsed;

        GuardiIcon.Visibility = page.Mascot == MascotKind.Guardi ? Visibility.Visible : Visibility.Collapsed;
        TrustedSubIcon.Visibility = page.Mascot == MascotKind.TrustedSub ? Visibility.Visible : Visibility.Collapsed;
        RestrictedSubIcon.Visibility = page.Mascot == MascotKind.RestrictedSub ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = _pageIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        var isLast = _pageIndex == _pages.Length - 1;
        NextButton.Content = isLast ? UiCopy.TourFinish : UiCopy.TourNext;

        for (var i = 0; i < _dots.Length; i++)
        {
            _dots[i].Fill = i == _pageIndex
                ? (Brush)FindResource("PrimaryDarkBrush")
                : (Brush)FindResource("SkyBorderBrush");
        }
    }
}
