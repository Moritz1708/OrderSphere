namespace OrderSphere.Web.Tests.Services;

/// <summary>
/// Guards the legibility of the palette. These are the numbers the design system
/// documents; if a token is retuned, this test is the place that says whether the
/// new value is still usable, and for what.
/// </summary>
public sealed class DesignTokensContrastTests
{
    private const double BodyText = 4.5;   // WCAG AA, text below 24px
    private const double LargeText = 3.0;  // WCAG AA, >=24px or >=19px bold, and UI borders

    public static TheoryData<string, string, string> BodyTextPairs() => new()
    {
        // description,                       foreground,                     background
        { "light ink on canvas",              DesignTokens.Light.Ink,         DesignTokens.Light.Canvas },
        { "light ink on paper",               DesignTokens.Light.Ink,         DesignTokens.Light.Paper },
        { "light ink on sunk",                DesignTokens.Light.Ink,         DesignTokens.Light.Sunk },
        { "light muted on canvas",            DesignTokens.Light.Muted,       DesignTokens.Light.Canvas },
        { "light muted on paper",             DesignTokens.Light.Muted,       DesignTokens.Light.Paper },
        { "light muted on sunk",              DesignTokens.Light.Muted,       DesignTokens.Light.Sunk },
        { "light accent-ink on canvas",       DesignTokens.Light.AccentInk,   DesignTokens.Light.Canvas },
        { "light accent-ink on paper",        DesignTokens.Light.AccentInk,   DesignTokens.Light.Paper },
        { "light cobalt on canvas",           DesignTokens.Light.Accent2,     DesignTokens.Light.Canvas },
        { "light cobalt on paper",            DesignTokens.Light.Accent2,     DesignTokens.Light.Paper },
        { "light success on canvas",          DesignTokens.Light.Success,     DesignTokens.Light.Canvas },
        { "light warning on canvas",          DesignTokens.Light.Warning,     DesignTokens.Light.Canvas },
        { "light error on canvas",            DesignTokens.Light.Error,       DesignTokens.Light.Canvas },
        { "light ink on accent fill",         DesignTokens.Light.OnAccent,    DesignTokens.Light.Accent },
        { "light canvas on ink block",        DesignTokens.Light.Canvas,      DesignTokens.Light.Ink },

        { "dark ink on canvas",               DesignTokens.Dark.Ink,          DesignTokens.Dark.Canvas },
        { "dark ink on paper",                DesignTokens.Dark.Ink,          DesignTokens.Dark.Paper },
        { "dark ink on sunk",                 DesignTokens.Dark.Ink,          DesignTokens.Dark.Sunk },
        { "dark muted on canvas",             DesignTokens.Dark.Muted,        DesignTokens.Dark.Canvas },
        { "dark muted on paper",              DesignTokens.Dark.Muted,        DesignTokens.Dark.Paper },
        { "dark muted on sunk",               DesignTokens.Dark.Muted,        DesignTokens.Dark.Sunk },
        { "dark accent-ink on canvas",        DesignTokens.Dark.AccentInk,    DesignTokens.Dark.Canvas },
        { "dark accent-ink on paper",         DesignTokens.Dark.AccentInk,    DesignTokens.Dark.Paper },
        { "dark cobalt on canvas",            DesignTokens.Dark.Accent2,      DesignTokens.Dark.Canvas },
        { "dark success on canvas",           DesignTokens.Dark.Success,      DesignTokens.Dark.Canvas },
        { "dark warning on canvas",           DesignTokens.Dark.Warning,      DesignTokens.Dark.Canvas },
        { "dark error on canvas",             DesignTokens.Dark.Error,        DesignTokens.Dark.Canvas },
        { "dark canvas on accent fill",       DesignTokens.Dark.OnAccent,     DesignTokens.Dark.Accent },
    };

    [Theory]
    [MemberData(nameof(BodyTextPairs))]
    public void BodySizedText_MeetsAa(string description, string foreground, string background)
    {
        WcagContrast.Ratio(foreground, background)
            .Should().BeGreaterThanOrEqualTo(BodyText, "{0} carries text below 24px", description);
    }

    public static TheoryData<string, string, string> LargeTextPairs() => new()
    {
        // The vivid accent is deliberately below 4.5 on canvas. It is allowed on
        // display headlines and fills only; --os-accent-ink covers everything else.
        { "light accent on canvas", DesignTokens.Light.Accent, DesignTokens.Light.Canvas },
        { "light accent on paper",  DesignTokens.Light.Accent, DesignTokens.Light.Paper },
        { "dark accent on canvas",  DesignTokens.Dark.Accent,  DesignTokens.Dark.Canvas },
        { "dark accent on paper",   DesignTokens.Dark.Accent,  DesignTokens.Dark.Paper },
    };

    [Theory]
    [MemberData(nameof(LargeTextPairs))]
    public void DisplaySizedAccent_MeetsAaLarge(string description, string foreground, string background)
    {
        WcagContrast.Ratio(foreground, background)
            .Should().BeGreaterThanOrEqualTo(LargeText, "{0} is used at display sizes", description);
    }

    [Fact]
    public void VividAccent_IsNotUsedForBodyText()
    {
        // Documents the reason --os-accent-ink exists. If this ever passes 4.5,
        // the accent got darker and the second token may be worth retiring.
        WcagContrast.Ratio(DesignTokens.Light.Accent, DesignTokens.Light.Canvas)
            .Should().BeLessThan(BodyText,
                "the light accent is a fill colour; --os-accent-ink is the text-safe variant");
    }

    [Fact]
    public void OnAccent_BeatsWhiteOnAccent()
    {
        var ink = WcagContrast.Ratio(DesignTokens.Light.OnAccent, DesignTokens.Light.Accent);
        var white = WcagContrast.Ratio("#FFFFFF", DesignTokens.Light.Accent);

        ink.Should().BeGreaterThan(white,
            "labels on an accent fill are ink, never white");
    }

    [Theory]
    [InlineData("light hairline", DesignTokens.Light.BorderControl, DesignTokens.Light.Canvas)]
    [InlineData("dark hairline", DesignTokens.Dark.BorderControl, DesignTokens.Dark.Canvas)]
    public void InputBorders_AreVisible(string description, string border, string background)
    {
        // Non-text UI components need 3:1 under WCAG 2.1 SC 1.4.11. Input borders
        // use the strong hairline for exactly this reason.
        WcagContrast.Ratio(border, background)
            .Should().BeGreaterThanOrEqualTo(LargeText, "{0} outlines form controls", description);
    }
}
