using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Services;

internal static class ThemeService
{
    public static void Apply(ModeTheme theme, ModeUiPresentation? presentation = null)
    {
        if (Application.Current is null)
            return;

        var resources = Application.Current.Resources;
        SetBrush(resources, "PrimaryBrush", theme.Primary);
        SetBrush(resources, "PrimaryDarkBrush", theme.PrimaryDark);
        SetBrush(resources, "AccentBrush", theme.AccentBackground);
        SetBrush(resources, "PageBrush", theme.Page);
        SetBrush(resources, "TextBrush", theme.Text);
        SetBrush(resources, "MutedBrush", theme.Muted);
        SetBrush(resources, "SkyBorderBrush", theme.SkyBorder);
        SetBrush(resources, "SkySoftBrush", theme.SkySoft);
        SetBrush(resources, "OnPrimaryMutedBrush", theme.OnPrimaryMuted);
        SetBrush(resources, "OnPrimaryBrush", theme.OnPrimary);
        SetBrush(resources, "CardBrush", theme.CardBackground);
        SetBrush(resources, "GlassCardBrush", theme.GlassBackground);
        SetBrush(resources, "GlassBorderBrush", theme.GlassBorder);
        SetBrush(resources, "GlassDividerBrush", theme.GlassDivider);
        SetBrush(resources, "GlassProgressTrackBrush", theme.ProgressTrack);
        SetBrush(resources, "HomeSurfaceBrush", theme.ChipBackground);
        SetBrush(resources, "HomeSurfaceBorderBrush", theme.ChipBorder);
        SetBrush(resources, "HomeHeroAccentBrush", theme.HeroAccent);
        SetBrush(resources, "OnlineBrush", theme.Online);
        SetBrush(resources, "PageAtmosphereBrush", theme.Atmosphere);
        SetBrush(resources, "SegmentTrackBrush", theme.ChipBackground);
        SetBrush(resources, "SegmentBorderBrush", theme.ChipBorder);
        SetBrush(resources, "SegmentInactiveBrush", theme.ChipBackground);
        SetBrush(resources, "SegmentInactiveForegroundBrush", theme.Text);
        SetBrush(resources, "SegmentActiveBrush", theme.Primary);
        SetBrush(resources, "SegmentActiveForegroundBrush", theme.OnPrimary);
        SetBrush(resources, "SegmentPastBrush", theme.AccentBackground);
        SetGradient(resources, "HeroGradient", theme.HeroStart, theme.HeroEnd);
        SetGradient(resources, "LockOverlayGradient", theme.LockOverlayStart, theme.LockOverlayEnd);
        SetGradient(resources, "PageGradient", theme.PageGradientStart, theme.PageGradientEnd);

        // Tech/sci-fi modes get a soft neon glow on live status indicators (online dot, progress
        // fill); calmer modes get an invisible placeholder so the DynamicResource Effect binding
        // always resolves without changing their current look.
        var isGlowMode = string.Equals(theme.Primary, ModeTheme.TrustedSub.Primary, StringComparison.OrdinalIgnoreCase);
        resources["StatusGlow"] = CreateGlow(theme.Primary, blurRadius: 9, opacity: isGlowMode ? 0.85 : 0.0);
        resources["ProgressGlow"] = CreateGlow(theme.Primary, blurRadius: 10, opacity: isGlowMode ? 0.55 : 0.0);
        resources["WindowShellShadow"] = CreateGlow(theme.ShellShadowColor, blurRadius: 48, opacity: theme.ShellShadowOpacity);

        if (presentation is not null)
        {
            resources["AppFontFamily"] = new FontFamily(presentation.FontFamily);
            resources["AppEyebrowFontFamily"] = new FontFamily(presentation.EyebrowFontFamily);
            resources["CardCornerRadius"] = new CornerRadius(presentation.CardCornerRadius);
            resources["SmallCornerRadius"] = new CornerRadius(presentation.SmallCornerRadius);
            resources["WindowCornerRadius"] = new CornerRadius(presentation.WindowCornerRadius);
            UiPresentationState.Apply(presentation);
        }
    }

    private static DropShadowEffect CreateGlow(string color, double blurRadius, double opacity) => new()
    {
        Color = (Color)ColorConverter.ConvertFromString(color)!,
        BlurRadius = blurRadius,
        ShadowDepth = 0,
        Opacity = opacity,
    };

    private static void SetBrush(ResourceDictionary resources, string key, string color)
    {
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
    }

    private static void SetGradient(ResourceDictionary resources, string key, string start, string end)
    {
        resources[key] = new LinearGradientBrush
        {
            StartPoint = key == "HeroGradient" ? new Point(0, 0) : new Point(0, 0),
            EndPoint = key == "HeroGradient" ? new Point(1, 1) : new Point(0, 1),
            GradientStops =
            [
                new GradientStop((Color)ColorConverter.ConvertFromString(start)!, 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(end)!, 1),
            ],
        };
    }
}
