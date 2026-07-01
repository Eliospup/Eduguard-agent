namespace EduGuardAgent.Profiles;

internal sealed class ModeTheme
{
    public required string Primary { get; init; }
    public required string PrimaryDark { get; init; }
    public required string Accent { get; init; }
    public required string Page { get; init; }
    public required string Text { get; init; }
    public required string Muted { get; init; }
    public required string SkyBorder { get; init; }
    public required string SkySoft { get; init; }
    public required string OnPrimaryMuted { get; init; }

    /// <summary>Primary text rendered directly on the page/header chrome (OnPrimaryBrush).</summary>
    public required string OnPrimary { get; init; }
    public required string HeroStart { get; init; }
    public required string HeroEnd { get; init; }
    public required string PageGradientStart { get; init; }
    public required string PageGradientEnd { get; init; }
    public required string LockOverlayStart { get; init; }
    public required string LockOverlayEnd { get; init; }

    /// <summary>Card surface (CardBrush) — the main solid card background.</summary>
    public required string CardBackground { get; init; }

    /// <summary>Glass-style card surface (GlassCardBrush).</summary>
    public required string GlassBackground { get; init; }

    /// <summary>Glass card border (GlassBorderBrush).</summary>
    public required string GlassBorder { get; init; }

    /// <summary>Divider line inside cards (GlassDividerBrush).</summary>
    public required string GlassDivider { get; init; }

    /// <summary>Progress bar track (GlassProgressTrackBrush).</summary>
    public required string ProgressTrack { get; init; }

    /// <summary>Small icon/chip surface (HomeSurfaceBrush).</summary>
    public required string ChipBackground { get; init; }

    /// <summary>Small icon/chip border (HomeSurfaceBorderBrush).</summary>
    public required string ChipBorder { get; init; }

    /// <summary>Hero card accent border (HomeHeroAccentBrush).</summary>
    public required string HeroAccent { get; init; }

    /// <summary>Soft accent panel background (AccentBrush — enrollment badges, etc.).</summary>
    public required string AccentBackground { get; init; }

    /// <summary>Ambient page wash color, used at low alpha (PageAtmosphereBrush). Full ARGB hex.</summary>
    public required string Atmosphere { get; init; }

    /// <summary>Online/connected status dot (OnlineBrush).</summary>
    public required string Online { get; init; }

    /// <summary>Tint of the soft ambient shadow cast around the window shell.</summary>
    public required string ShellShadowColor { get; init; }

    /// <summary>Opacity of the window shell shadow — colored shadows read stronger than black at the same value.</summary>
    public required double ShellShadowOpacity { get; init; }

    /** Sub - bright Guardi sky blue, light clean chrome. */
    public static ModeTheme Sub { get; } = new()
    {
        Primary = "#0EA5E9",
        PrimaryDark = "#075985",
        Accent = "#E0F2FE",
        Page = "#F8FAFC",
        Text = "#1F2A44",
        Muted = "#64748B",
        SkyBorder = "#BAE6FD",
        SkySoft = "#F5F8FF",
        OnPrimaryMuted = "#5B7186",
        OnPrimary = "#1F2A44",
        HeroStart = "#0EA5E9",
        HeroEnd = "#38BDF8",
        PageGradientStart = "#D6ECFC",
        PageGradientEnd = "#F2F9FF",
        LockOverlayStart = "#0EA5E9",
        LockOverlayEnd = "#38BDF8",
        CardBackground = "#FFFFFF",
        GlassBackground = "#F8FAFC",
        GlassBorder = "#CAD7EA",
        GlassDivider = "#D7E2F2",
        ProgressTrack = "#D7E2F2",
        ChipBackground = "#F5F8FF",
        ChipBorder = "#D8E7FF",
        HeroAccent = "#38BDF8",
        AccentBackground = "#EEF4FF",
        Atmosphere = "#0A38BDF8",
        Online = "#0EA5E9",
        ShellShadowColor = "#0EA5E9",
        ShellShadowOpacity = 0.22,
    };

    /** Trusted Sub - secure focus console: near-black HUD, neon teal, warm amber rest accent. */
    public static ModeTheme TrustedSub { get; } = new()
    {
        Primary = "#33E6CD",
        PrimaryDark = "#1FA897",
        Accent = "#11202A",
        Page = "#F8FAFC",
        Text = "#E7F1F2",
        Muted = "#7E97A1",
        SkyBorder = "#1FA897",
        SkySoft = "#0F1822",
        OnPrimaryMuted = "#BFE3DD",
        OnPrimary = "#FFFFFF",
        HeroStart = "#33E6CD",
        HeroEnd = "#1FA897",
        PageGradientStart = "#0B1014",
        PageGradientEnd = "#070A0D",
        LockOverlayStart = "#1FA897",
        LockOverlayEnd = "#F4B458",
        CardBackground = "#0F1822",
        GlassBackground = "#0F1822",
        GlassBorder = "#1C3640",
        GlassDivider = "#1C3640",
        ProgressTrack = "#142530",
        ChipBackground = "#11202A",
        ChipBorder = "#1C3640",
        HeroAccent = "#1FA897",
        AccentBackground = "#11202A",
        Atmosphere = "#1233E6CD",
        Online = "#33E6CD",
        ShellShadowColor = "#000000",
        ShellShadowOpacity = 0.38,
    };

    /** Restricted Sub - amber containment: warm charcoal lockdown, amber-forward, watched but enveloping. */
    public static ModeTheme RestrictedSub { get; } = new()
    {
        Primary = "#F5A623",
        PrimaryDark = "#E0941A",
        Accent = "#2B2114",
        Page = "#F8FAFC",
        Text = "#F3E9DC",
        Muted = "#A8967E",
        SkyBorder = "#8A6A2E",
        SkySoft = "#221C15",
        OnPrimaryMuted = "#D9C7A8",
        OnPrimary = "#FFFFFF",
        HeroStart = "#F5A623",
        HeroEnd = "#C77F12",
        PageGradientStart = "#1B1510",
        PageGradientEnd = "#100C08",
        LockOverlayStart = "#F5A623",
        LockOverlayEnd = "#C77F12",
        CardBackground = "#221C15",
        GlassBackground = "#221C15",
        GlassBorder = "#3A2E1F",
        GlassDivider = "#3A2E1F",
        ProgressTrack = "#2B2114",
        ChipBackground = "#2B2114",
        ChipBorder = "#3A2E1F",
        HeroAccent = "#F7B84D",
        AccentBackground = "#2B2114",
        Atmosphere = "#12F5A623",
        Online = "#F5A623",
        ShellShadowColor = "#000000",
        ShellShadowOpacity = 0.38,
    };
}
