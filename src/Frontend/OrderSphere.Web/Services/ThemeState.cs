using MudBlazor;

namespace OrderSphere.Web.Services;

/// <summary>
/// Holds the dark-mode flag and the single MudBlazor theme. There is exactly one
/// light and one dark palette; both are built from <see cref="DesignTokens"/>.
/// <para>
/// The theme is static because nothing about it varies at runtime — dark mode is
/// a palette switch inside <c>MudThemeProvider</c>, not a rebuild. The CSS side
/// switches on <c>html[data-theme]</c>, which <c>theme-boot.js</c> sets before
/// the first paint and <c>IMotionService.SetThemeAsync</c> keeps in sync.
/// </para>
/// </summary>
public sealed class ThemeState
{
    /// <summary>The one theme. Dark mode selects <see cref="MudTheme.PaletteDark"/> from it.</summary>
    public static readonly MudTheme Theme = BuildTheme();

    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    public void Toggle() => SetDarkMode(!IsDarkMode);

    /// <summary>Sets dark mode to a known value (restoring a stored preference). No-op if unchanged.</summary>
    public void SetDarkMode(bool enabled)
    {
        if (IsDarkMode == enabled)
            return;

        IsDarkMode = enabled;
        OnChange?.Invoke();
    }

    private static MudTheme BuildTheme() => new()
    {
        PaletteLight = BuildLight(),
        PaletteDark = BuildDark(),
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = DesignTokens.Layout.RadiusControl,
            AppbarHeight = DesignTokens.Layout.HeaderHeight,
            DrawerWidthLeft = DesignTokens.Layout.DrawerWidthLeft,
            DrawerWidthRight = DesignTokens.Layout.DrawerWidthRight,
        },
        // Separation comes from hairlines and a single hover shadow, never from
        // Material elevation — so every elevation step is flattened.
        Shadows = new Shadow { Elevation = [.. Enumerable.Repeat("none", 26)] },
        Typography = BuildTypography(),
    };

    private static Typography BuildTypography() => new()
    {
        Default = new DefaultTypography
        {
            FontFamily = DesignTokens.Fonts.Body,
            FontSize = "1rem",
            FontWeight = "400",
            LineHeight = "1.6",
        },
        H1 = new H1Typography
        {
            FontFamily = DesignTokens.Fonts.Display,
            FontSize = "clamp(2.75rem, 7vw, 4.5rem)",
            FontWeight = "400",
            LineHeight = "0.95",
            LetterSpacing = "-0.01em",
        },
        H2 = new H2Typography
        {
            FontFamily = DesignTokens.Fonts.Display,
            FontSize = "clamp(2.25rem, 5vw, 3.5rem)",
            FontWeight = "400",
            LineHeight = "1",
            LetterSpacing = "-0.01em",
        },
        H3 = new H3Typography
        {
            FontFamily = DesignTokens.Fonts.Display,
            FontSize = "clamp(1.75rem, 3.5vw, 2.5rem)",
            FontWeight = "400",
            LineHeight = "1.05",
            LetterSpacing = "-0.01em",
        },
        H4 = new H4Typography
        {
            FontFamily = DesignTokens.Fonts.Display,
            FontSize = "1.75rem",
            FontWeight = "400",
            LineHeight = "1.1",
        },
        H5 = new H5Typography
        {
            FontFamily = DesignTokens.Fonts.Body,
            FontSize = "1.375rem",
            FontWeight = "600",
            LineHeight = "1.3",
        },
        H6 = new H6Typography
        {
            FontFamily = DesignTokens.Fonts.Body,
            FontSize = "1.125rem",
            FontWeight = "600",
            LineHeight = "1.3",
        },
        Subtitle1 = new Subtitle1Typography
        {
            FontFamily = DesignTokens.Fonts.Body,
            FontSize = "1rem",
            FontWeight = "600",
        },
        Subtitle2 = new Subtitle2Typography
        {
            FontFamily = DesignTokens.Fonts.Body,
            FontSize = "0.875rem",
            FontWeight = "600",
        },
        Body1 = new Body1Typography { FontFamily = DesignTokens.Fonts.Body, FontSize = "1rem" },
        Body2 = new Body2Typography { FontFamily = DesignTokens.Fonts.Body, FontSize = "0.875rem" },
        Button = new ButtonTypography
        {
            FontFamily = DesignTokens.Fonts.Body,
            FontSize = "0.9375rem",
            FontWeight = "600",
            TextTransform = "none",
        },
        Caption = new CaptionTypography { FontFamily = DesignTokens.Fonts.Body, FontSize = "0.75rem" },
        Overline = new OverlineTypography
        {
            FontFamily = DesignTokens.Fonts.Mono,
            FontSize = "0.72rem",
            FontWeight = "500",
            LetterSpacing = "0.12em",
            TextTransform = "uppercase",
        },
    };

    private static PaletteLight BuildLight() => new()
    {
        Primary = DesignTokens.Light.Accent,
        PrimaryContrastText = DesignTokens.Light.OnAccent,
        PrimaryDarken = DesignTokens.Light.AccentDarken,
        PrimaryLighten = DesignTokens.Light.AccentLighten,

        Secondary = DesignTokens.Light.Ink,
        SecondaryContrastText = DesignTokens.Light.Canvas,

        // Cobalt is a genuine slot now — it is no longer standing in for
        // "brand-coloured text", which the old multi-brand system needed.
        Tertiary = DesignTokens.Light.Accent2,
        TertiaryContrastText = "#FFFFFF",
        Info = DesignTokens.Light.Accent2,
        InfoContrastText = "#FFFFFF",

        Success = DesignTokens.Light.Success,
        SuccessContrastText = "#FFFFFF",
        Warning = DesignTokens.Light.WarningFill,
        WarningContrastText = DesignTokens.Light.Ink,
        Error = DesignTokens.Light.Error,
        ErrorContrastText = "#FFFFFF",

        Background = DesignTokens.Light.Canvas,
        BackgroundGray = DesignTokens.Light.Sunk,
        Surface = DesignTokens.Light.Paper,

        TextPrimary = DesignTokens.Light.Ink,
        TextSecondary = DesignTokens.Light.Muted,
        TextDisabled = DesignTokens.Light.TextDisabled,

        Divider = DesignTokens.Light.Hairline,
        DividerLight = "rgba(20,18,15,0.08)",
        LinesDefault = DesignTokens.Light.Hairline,
        LinesInputs = DesignTokens.Light.BorderControl,
        TableLines = DesignTokens.Light.Hairline,
        TableStriped = DesignTokens.Light.Sunk,
        TableHover = DesignTokens.Light.Sunk,

        AppbarBackground = DesignTokens.Light.AppbarBackground,
        AppbarText = DesignTokens.Light.Ink,
        DrawerBackground = DesignTokens.Light.Paper,
        DrawerText = DesignTokens.Light.Ink,
        DrawerIcon = DesignTokens.Light.Ink,

        ActionDefault = DesignTokens.Light.Ink,
        ActionDisabled = DesignTokens.Light.TextDisabled,
        ActionDisabledBackground = "rgba(20,18,15,0.08)",

        Skeleton = DesignTokens.Light.Skeleton,
        OverlayDark = DesignTokens.Light.OverlayDark,
        OverlayLight = DesignTokens.Light.OverlayLight,

        HoverOpacity = 0.06,
        // Material ripple has no place in a flat editorial surface.
        RippleOpacity = 0,
        RippleOpacitySecondary = 0,
    };

    private static PaletteDark BuildDark() => new()
    {
        Primary = DesignTokens.Dark.Accent,
        PrimaryContrastText = DesignTokens.Dark.OnAccent,
        PrimaryDarken = DesignTokens.Dark.AccentDarken,
        PrimaryLighten = DesignTokens.Dark.AccentLighten,

        Secondary = DesignTokens.Dark.Ink,
        SecondaryContrastText = DesignTokens.Dark.Canvas,

        Tertiary = DesignTokens.Dark.Accent2,
        TertiaryContrastText = DesignTokens.Dark.Canvas,
        Info = DesignTokens.Dark.Accent2,
        InfoContrastText = DesignTokens.Dark.Canvas,

        Success = DesignTokens.Dark.Success,
        SuccessContrastText = DesignTokens.Dark.Canvas,
        Warning = DesignTokens.Dark.WarningFill,
        WarningContrastText = DesignTokens.Dark.Canvas,
        Error = DesignTokens.Dark.Error,
        ErrorContrastText = DesignTokens.Dark.Canvas,

        Background = DesignTokens.Dark.Canvas,
        BackgroundGray = DesignTokens.Dark.Sunk,
        Surface = DesignTokens.Dark.Paper,

        TextPrimary = DesignTokens.Dark.Ink,
        TextSecondary = DesignTokens.Dark.Muted,
        TextDisabled = DesignTokens.Dark.TextDisabled,

        Divider = DesignTokens.Dark.Hairline,
        DividerLight = "rgba(242,239,233,0.08)",
        LinesDefault = DesignTokens.Dark.Hairline,
        LinesInputs = DesignTokens.Dark.BorderControl,
        TableLines = DesignTokens.Dark.Hairline,
        TableStriped = DesignTokens.Dark.Sunk,
        TableHover = DesignTokens.Dark.Sunk,

        AppbarBackground = DesignTokens.Dark.AppbarBackground,
        AppbarText = DesignTokens.Dark.Ink,
        DrawerBackground = DesignTokens.Dark.Paper,
        DrawerText = DesignTokens.Dark.Ink,
        DrawerIcon = DesignTokens.Dark.Ink,

        ActionDefault = DesignTokens.Dark.Ink,
        ActionDisabled = DesignTokens.Dark.TextDisabled,
        ActionDisabledBackground = "rgba(242,239,233,0.08)",

        Skeleton = DesignTokens.Dark.Skeleton,
        OverlayDark = DesignTokens.Dark.OverlayDark,
        OverlayLight = DesignTokens.Dark.OverlayLight,

        HoverOpacity = 0.06,
        RippleOpacity = 0,
        RippleOpacitySecondary = 0,
    };
}
