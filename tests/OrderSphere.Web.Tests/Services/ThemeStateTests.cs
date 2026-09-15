namespace OrderSphere.Web.Tests.Services;

public sealed class ThemeStateTests
{
    [Fact]
    public void Default_IsLightMode()
    {
        var sut = new ThemeState();

        sut.IsDarkMode.Should().BeFalse();
    }

    [Fact]
    public void Theme_IsSharedAcrossInstances()
    {
        var first = ThemeState.Theme;
        var second = ThemeState.Theme;

        second.Should().BeSameAs(first,
            "the theme no longer varies at runtime, so it is built once");
    }

    [Fact]
    public void Toggle_FlipsDarkMode_AndRaisesOnChange()
    {
        var sut = new ThemeState();
        var raised = false;
        sut.OnChange += () => raised = true;

        sut.Toggle();

        sut.IsDarkMode.Should().BeTrue();
        raised.Should().BeTrue();
    }

    [Fact]
    public void Toggle_Twice_ReturnsToLight()
    {
        var sut = new ThemeState();

        sut.Toggle();
        sut.Toggle();

        sut.IsDarkMode.Should().BeFalse();
    }

    [Fact]
    public void SetDarkMode_True_SetsFlag_AndRaisesOnChange()
    {
        var sut = new ThemeState();
        var raised = false;
        sut.OnChange += () => raised = true;

        sut.SetDarkMode(true);

        sut.IsDarkMode.Should().BeTrue();
        raised.Should().BeTrue();
    }

    [Fact]
    public void SetDarkMode_SameValue_DoesNotRaiseOnChange()
    {
        var sut = new ThemeState();
        sut.SetDarkMode(true);
        var raised = false;
        sut.OnChange += () => raised = true;

        sut.SetDarkMode(true);

        raised.Should().BeFalse();
    }

    [Fact]
    public void PaletteLight_UsesTheEditorialAccentAndInk()
    {
        var palette = ThemeState.Theme.PaletteLight;

        palette.Primary.Should().Be(new MudColor(DesignTokens.Light.Accent));
        palette.Background.Should().Be(new MudColor(DesignTokens.Light.Canvas));
        palette.Surface.Should().Be(new MudColor(DesignTokens.Light.Paper));
        palette.TextPrimary.Should().Be(new MudColor(DesignTokens.Light.Ink));
    }

    [Fact]
    public void PaletteDark_UsesTheLiftedAccentAndInvertedSurfaces()
    {
        var palette = ThemeState.Theme.PaletteDark;

        palette.Primary.Should().Be(new MudColor(DesignTokens.Dark.Accent));
        palette.Background.Should().Be(new MudColor(DesignTokens.Dark.Canvas));
        palette.Surface.Should().Be(new MudColor(DesignTokens.Dark.Paper));
        palette.TextPrimary.Should().Be(new MudColor(DesignTokens.Dark.Ink));
    }

    [Fact]
    public void Palettes_DoNotRepurposeTertiary()
    {
        // The multi-brand system used Tertiary as a "brand text" slot; it is a
        // genuine cobalt accent now, matching Info.
        ThemeState.Theme.PaletteLight.Tertiary.Should().Be(new MudColor(DesignTokens.Light.Accent2));
        ThemeState.Theme.PaletteLight.Info.Should().Be(new MudColor(DesignTokens.Light.Accent2));
    }

    [Fact]
    public void Shadows_AreAllNone()
    {
        // Separation comes from hairlines and one hover shadow, never elevation.
        ThemeState.Theme.Shadows.Elevation.Should().OnlyContain(s => s == "none");
    }

    [Fact]
    public void Ripple_IsDisabled()
    {
        ThemeState.Theme.PaletteLight.RippleOpacity.Should().Be(0);
        ThemeState.Theme.PaletteDark.RippleOpacity.Should().Be(0);
    }

    [Fact]
    public void Typography_UsesSerifForDisplayAndManropeForBody()
    {
        ThemeState.Theme.Typography.H1!.FontFamily.Should().StartWith(["Instrument Serif"]);
        ThemeState.Theme.Typography.H2!.FontFamily.Should().StartWith(["Instrument Serif"]);
        ThemeState.Theme.Typography.Default!.FontFamily.Should().StartWith(["Manrope"]);
        ThemeState.Theme.Typography.Button!.FontFamily.Should().StartWith(["Manrope"]);
    }

    [Fact]
    public void Typography_NeverUppercasesButtons()
    {
        ThemeState.Theme.Typography.Button!.TextTransform.Should().Be("none");
    }

    [Fact]
    public void Layout_MatchesTheHeaderHeightUsedByCss()
    {
        // A mismatch here reopens the old 72px-vs-64px content-offset bug.
        ThemeState.Theme.LayoutProperties.AppbarHeight.Should().Be(DesignTokens.Layout.HeaderHeight);
        ThemeState.Theme.LayoutProperties.DefaultBorderRadius.Should().Be(DesignTokens.Layout.RadiusControl);
    }
}
