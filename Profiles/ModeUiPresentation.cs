namespace EduGuardAgent.Profiles;

internal enum DesktopWidgetVisual
{
    GuardiMascot,
    SoberShield,
    LockedShield,
}

internal enum MascotKind
{
    Guardi,
    TrustedSubBackpack,
    RestrictedSubLock,
}

internal sealed class ModeUiPresentation
{
    public bool ShowMascot { get; init; } = true;
    public MascotKind Mascot { get; init; } = MascotKind.Guardi;
    public bool ShowDesktopWidget { get; init; } = true;
    public string HeaderIconGlyph { get; init; } = "";
    public string WidgetLabel { get; init; } = "";
    public DesktopWidgetVisual WidgetVisual { get; init; } = DesktopWidgetVisual.GuardiMascot;
    public double WidgetWidth { get; init; } = 96;
    public double WidgetHeight { get; init; } = 138;
    public double WidgetIconWidth { get; init; } = 72;
    public double WidgetIconHeight { get; init; } = 82;
    public string FontFamily { get; init; } = "Candara, Segoe UI, sans-serif";

    /// <summary>Mono/technical face for HUD-style eyebrow labels and status readouts.</summary>
    public string EyebrowFontFamily { get; init; } = "Candara, Segoe UI, sans-serif";
    public double CardCornerRadius { get; init; } = 28;
    public double SmallCornerRadius { get; init; } = 18;
    public double WindowCornerRadius { get; init; } = 24;
    public bool UseOutlinedCards { get; init; }

    /// <summary>Trusted Sub — secure focus console: dark HUD, neon teal, tech/sci-fi but reassuring.</summary>
    public static ModeUiPresentation Study { get; } = new()
    {
        ShowMascot = true,
        Mascot = MascotKind.TrustedSubBackpack,
        ShowDesktopWidget = true,
        WidgetLabel = "Study",
        WidgetVisual = DesktopWidgetVisual.SoberShield,
        FontFamily = "pack://application:,,,/EduGuardAgent;component/Fonts/#Chakra Petch, Segoe UI, sans-serif",
        EyebrowFontFamily = "pack://application:,,,/EduGuardAgent;component/Fonts/#Share Tech Mono, Consolas, monospace",
        CardCornerRadius = 26,
        SmallCornerRadius = 18,
        WindowCornerRadius = 22,
    };

    /// <summary>Sub — full Guardi playfulness.</summary>
    public static ModeUiPresentation Playful { get; } = new()
    {
        ShowMascot = true,
        Mascot = MascotKind.Guardi,
        ShowDesktopWidget = true,
        WidgetVisual = DesktopWidgetVisual.GuardiMascot,
        FontFamily = "Candara, Segoe UI, sans-serif",
        CardCornerRadius = 28,
        SmallCornerRadius = 18,
        WindowCornerRadius = 26,
    };

    /// <summary>Restricted Sub — amber containment: dark warm lockdown, smiling-lock mascot.</summary>
    public static ModeUiPresentation SecurePlayful { get; } = new()
    {
        ShowMascot = true,
        Mascot = MascotKind.RestrictedSubLock,
        ShowDesktopWidget = true,
        WidgetLabel = "Guardi",
        WidgetVisual = DesktopWidgetVisual.LockedShield,
        FontFamily = "Candara, Segoe UI Semibold, sans-serif",
        CardCornerRadius = 22,
        SmallCornerRadius = 14,
        WindowCornerRadius = 20,
        UseOutlinedCards = true,
    };

    /** @deprecated Use Study — kept for callers that referenced Mature. */
    public static ModeUiPresentation Mature => Study;

    /** @deprecated Use SecurePlayful. */
    public static ModeUiPresentation Strict => SecurePlayful;
}

internal static class UiPresentationState
{
    public static ModeUiPresentation Current { get; private set; } = ModeUiPresentation.Playful;

    public static void Apply(ModeUiPresentation presentation) => Current = presentation;

    public static void ApplyMascotVisibility(
        System.Windows.UIElement guardiMascot,
        System.Windows.UIElement trustedSubMascot,
        System.Windows.UIElement restrictedSubMascot)
    {
        var show = Current.ShowMascot;
        guardiMascot.Visibility = show && Current.Mascot == MascotKind.Guardi
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        trustedSubMascot.Visibility = show && Current.Mascot == MascotKind.TrustedSubBackpack
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        restrictedSubMascot.Visibility = show && Current.Mascot == MascotKind.RestrictedSubLock
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }
}
