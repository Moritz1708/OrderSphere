using System.Text.RegularExpressions;

namespace OrderSphere.Web.Tests.Services;

/// <summary>
/// The palette exists twice: as C# constants for the MudBlazor theme, and as CSS
/// custom properties for everything else. That duplication is deliberate — CSS
/// cannot read C#, and <c>MudColor</c> cannot parse <c>color-mix()</c> — but it
/// has to stay honest, which is what these tests are for.
/// </summary>
public sealed class TokensCssSyncTests
{
    private static readonly string Css = ReadTokensCss();

    /// <summary>Every colour the theme uses must appear in the stylesheet.</summary>
    public static TheoryData<string, string> Colours() => new()
    {
        { "--os-canvas (light)", DesignTokens.Light.Canvas },
        { "--os-paper (light)", DesignTokens.Light.Paper },
        { "--os-sunk (light)", DesignTokens.Light.Sunk },
        { "--os-ink (light)", DesignTokens.Light.Ink },
        { "--os-muted (light)", DesignTokens.Light.Muted },
        { "--os-accent (light)", DesignTokens.Light.Accent },
        { "--os-accent-ink (light)", DesignTokens.Light.AccentInk },
        { "--os-on-accent (light)", DesignTokens.Light.OnAccent },
        { "--os-accent-2 (light)", DesignTokens.Light.Accent2 },
        { "--os-success (light)", DesignTokens.Light.Success },
        { "--os-warning (light)", DesignTokens.Light.Warning },
        { "--os-warning-fill (light)", DesignTokens.Light.WarningFill },
        { "--os-error (light)", DesignTokens.Light.Error },

        { "--os-canvas (dark)", DesignTokens.Dark.Canvas },
        { "--os-paper (dark)", DesignTokens.Dark.Paper },
        { "--os-sunk (dark)", DesignTokens.Dark.Sunk },
        { "--os-ink (dark)", DesignTokens.Dark.Ink },
        { "--os-muted (dark)", DesignTokens.Dark.Muted },
        { "--os-accent (dark)", DesignTokens.Dark.Accent },
        { "--os-accent-2 (dark)", DesignTokens.Dark.Accent2 },
        { "--os-success (dark)", DesignTokens.Dark.Success },
        { "--os-warning (dark)", DesignTokens.Dark.WarningFill },
        { "--os-error (dark)", DesignTokens.Dark.Error },
    };

    [Theory]
    [MemberData(nameof(Colours))]
    public void CsharpColour_ExistsInTokensCss(string token, string value)
    {
        Css.Should().Contain(value, "{0} must hold the same value in tokens.css and DesignTokens", token);
    }

    [Theory]
    [InlineData("--os-radius-control", nameof(DesignTokens.Layout.RadiusControl))]
    [InlineData("--os-header-h", nameof(DesignTokens.Layout.HeaderHeight))]
    public void LayoutConstant_MatchesTokensCss(string cssVariable, string constantName)
    {
        var expected = constantName switch
        {
            nameof(DesignTokens.Layout.RadiusControl) => DesignTokens.Layout.RadiusControl,
            nameof(DesignTokens.Layout.HeaderHeight) => DesignTokens.Layout.HeaderHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(constantName)),
        };

        DeclaredValue(cssVariable).Should().Be(expected);
    }

    [Fact]
    public void BothThemesAreDeclared()
    {
        Css.Should().Contain(":root {", "the light palette is the default");
        Css.Should().Contain(":root[data-theme=\"dark\"]", "the dark palette hangs off the boot attribute");
    }

    [Fact]
    public void ColourSchemeIsDeclaredForBothThemes()
    {
        // Without color-scheme the browser paints native controls, form fields and
        // the scrollbar for the wrong theme.
        Css.Should().Contain("color-scheme: light");
        Css.Should().Contain("color-scheme: dark");
    }

    [Fact]
    public void MotionDurationsAndEasingAreTokens()
    {
        // motion.css and every transition depend on these existing.
        foreach (var token in new[] { "--os-dur-1", "--os-dur-2", "--os-dur-3", "--os-dur-4", "--os-ease", "--os-stagger" })
            Css.Should().Contain(token);
    }

    private static string DeclaredValue(string cssVariable)
    {
        var match = Regex.Match(Css, Regex.Escape(cssVariable) + @"\s*:\s*([^;]+);");
        match.Success.Should().BeTrue("{0} must be declared in tokens.css", cssVariable);
        return match.Groups[1].Value.Trim();
    }

    private static string ReadTokensCss()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "tokens.css");

        File.Exists(path).Should().BeTrue(
            "tokens.css is linked into the test project by OrderSphere.Web.Tests.csproj");

        return File.ReadAllText(path);
    }
}
