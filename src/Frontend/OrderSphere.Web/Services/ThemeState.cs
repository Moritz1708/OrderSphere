using MudBlazor;

namespace OrderSphere.Web.Services;

/// <summary>
/// A selectable design brand. Only the primary colour family varies between brands;
/// typography, layout, neutral greys and semantic colours (success/warning/error) are shared.
/// </summary>
public sealed record BrandDefinition(
    string Id,
    string Name,
    string Primary,
    string PrimaryDarken,
    string PrimaryLighten,
    string PrimaryContrastText = "#FFFFFF");

/// <summary>
/// Holds the active MudBlazor theme, the active brand, and the dark-mode flag.
/// Components subscribe to OnChange for reactive rendering.
/// The theme is built per brand; CSS primary tints follow automatically via color-mix.
/// </summary>
public sealed class ThemeState
{
    /// <summary>Available brands. Indigo is the default and the original "Flat &amp; Focused" identity.</summary>
    public static readonly IReadOnlyList<BrandDefinition> Brands =
    [
        new("electric", "Electric", "#6260FF", "#4A48CC", "#E4E4FF"),
        new("lime",     "Lime",     "#9FE870", "#163300", "#C4F5A7", "#163300"),
        new("sage",     "Sage",     "#BDD9D7", "#03363D", "#D8EDEC", "#03363D"),
        new("royal",    "Royal",    "#3447AA", "#253592", "#FBEAEB"),
        new("solar",    "Solar",    "#FCDB32", "#141D38", "#FEF08A", "#141D38"),
        new("mint",     "Mint",     "#34E0A1", "#000000", "#87EEC8", "#000000"),
    ];

    private static readonly string[] _fontFamily =
    [
        "Space Grotesk", "Inter", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif",
    ];

    private static readonly string[] _monoFamily =
    [
        "JetBrains Mono", "ui-monospace", "monospace",
    ];

    private static readonly LayoutProperties _sharedLayout = new()
    {
        DefaultBorderRadius = "12px",
        AppbarHeight = "72px",
    };

    private static readonly Typography _sharedTypography = new()
    {
        Default = new DefaultTypography
        {
            FontFamily = _fontFamily,
            FontSize = "0.9375rem",
            FontWeight = "400",
            LineHeight = "1.6",
        },
        H1 = new H1Typography
        {
            FontFamily = _fontFamily,
            FontSize = "3rem",
            FontWeight = "600",
            LineHeight = "1.05",
            LetterSpacing = "-0.035em",
        },
        H2 = new H2Typography
        {
            FontFamily = _fontFamily,
            FontSize = "2.25rem",
            FontWeight = "600",
            LineHeight = "1.1",
            LetterSpacing = "-0.025em",
        },
        H3 = new H3Typography
        {
            FontFamily = _fontFamily,
            FontSize = "1.875rem",
            FontWeight = "600",
            LetterSpacing = "-0.015em",
        },
        H4 = new H4Typography
        {
            FontFamily = _fontFamily,
            FontSize = "1.5rem",
            FontWeight = "600",
            LetterSpacing = "-0.01em",
        },
        H5 = new H5Typography { FontFamily = _fontFamily, FontSize = "1.25rem", FontWeight = "600" },
        H6 = new H6Typography { FontFamily = _fontFamily, FontSize = "1.0625rem", FontWeight = "600" },
        Button = new ButtonTypography
        {
            FontSize = "0.9375rem",
            FontWeight = "600",
            TextTransform = "none",
        },
    };

    private BrandDefinition _brand = Brands[0];

    public ThemeState() => Theme = BuildTheme(_brand);

    /// <summary>The active MudBlazor theme. Rebuilt whenever the brand changes.</summary>
    public MudTheme Theme { get; private set; }

    public BrandDefinition CurrentBrand => _brand;

    public IReadOnlyList<BrandDefinition> AvailableBrands => Brands;

    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    /// <summary>Switches the active brand by id. No-op for unknown or unchanged ids.</summary>
    public void SetBrand(string brandId)
    {
        var match = Brands.FirstOrDefault(b => b.Id == brandId);
        if (match is null || match.Id == _brand.Id)
            return;

        _brand = match;
        Theme = BuildTheme(_brand);
        OnChange?.Invoke();
    }

    public void Toggle()
    {
        IsDarkMode = !IsDarkMode;
        OnChange?.Invoke();
    }

    /// <summary>Sets dark mode to a known value (e.g. restoring a persisted preference). No-op if unchanged.</summary>
    public void SetDarkMode(bool enabled)
    {
        if (IsDarkMode == enabled)
            return;

        IsDarkMode = enabled;
        OnChange?.Invoke();
    }

    private static MudTheme BuildTheme(BrandDefinition b) => new()
    {
        PaletteLight = BuildLight(b),
        PaletteDark = BuildDark(b),
        LayoutProperties = _sharedLayout,
        Typography = _sharedTypography,
    };

    private static PaletteLight BuildLight(BrandDefinition b) => new()
    {
        Primary = b.Primary,
        PrimaryContrastText = b.PrimaryContrastText,
        PrimaryDarken = b.PrimaryDarken,
        PrimaryLighten = b.PrimaryLighten,
        Secondary = "#0D0D18",
        SecondaryContrastText = "#FFFFFF",
        Background = "#FFFFFF",
        BackgroundGray = "#F1F2F8",
        Surface = "#FFFFFF",
        TextPrimary = "#0D0D18",
        TextSecondary = "#5A5A72",
        TextDisabled = "rgba(13,13,24,0.30)",
        Success = "#2EA04B",
        Warning = "#FF9F0A",
        Error = "#FF3B30",
        Info = b.Primary,
        Divider = "#DCDDE6",
        DividerLight = "#E8E9F0",
        AppbarBackground = "rgba(255,255,255,0.92)",
        AppbarText = "#0D0D18",
        DrawerBackground = "#FFFFFF",
        DrawerText = "#0D0D18",
        DrawerIcon = "#0D0D18",
        ActionDefault = "#0D0D18",
    };

    private static PaletteDark BuildDark(BrandDefinition b) => new()
    {
        Primary = b.Primary,
        PrimaryContrastText = b.PrimaryContrastText,
        PrimaryDarken = b.PrimaryDarken,
        PrimaryLighten = b.PrimaryLighten,
        Secondary = "#EEEEF4",
        SecondaryContrastText = "#0D0E14",
        Background = "#0D0E14",
        BackgroundGray = "#15161F",
        Surface = "#1C1E2A",
        TextPrimary = "#EEEEF4",
        TextSecondary = "rgba(238,238,244,0.70)",
        TextDisabled = "rgba(238,238,244,0.28)",
        Success = "#3DB85F",
        Warning = "#FF9F0A",
        Error = "#FF453A",
        Info = b.Primary,
        Divider = "rgba(238,238,244,0.14)",
        DividerLight = "rgba(238,238,244,0.08)",
        AppbarBackground = "rgba(13,14,20,0.88)",
        AppbarText = "#EEEEF4",
        DrawerBackground = "#1C1E2A",
        DrawerText = "#EEEEF4",
        DrawerIcon = "#EEEEF4",
        ActionDefault = "#EEEEF4",
    };
}
