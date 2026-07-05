namespace OrderSphere.Web.Tests.Services;

public sealed class ThemeStateTests
{
    [Fact]
    public void DefaultBrand_IsElectric()
    {
        var sut = new ThemeState();

        sut.CurrentBrand.Id.Should().Be("electric");
        sut.Theme.Should().NotBeNull();
    }

    [Fact]
    public void AvailableBrands_AreAllSix()
    {
        var sut = new ThemeState();

        sut.AvailableBrands.Select(b => b.Id)
           .Should().Equal("electric", "lime", "sage", "royal", "solar", "mint");
    }

    [Fact]
    public void SetBrand_Lime_SwitchesBrandAndRebuildsTheme()
    {
        var sut = new ThemeState();
        var before = sut.Theme;

        sut.SetBrand("lime");

        sut.CurrentBrand.Id.Should().Be("lime");
        sut.CurrentBrand.Primary.Should().Be("#9FE870");
        sut.Theme.Should().NotBeSameAs(before, "the MudTheme is rebuilt for the new brand");
    }

    [Fact]
    public void SetBrand_RaisesOnChange()
    {
        var sut = new ThemeState();
        var raised = false;
        sut.OnChange += () => raised = true;

        sut.SetBrand("royal");

        raised.Should().BeTrue();
    }

    [Fact]
    public void SetBrand_UnknownId_IsNoOp()
    {
        var sut = new ThemeState();
        var before = sut.Theme;
        var raised = false;
        sut.OnChange += () => raised = true;

        sut.SetBrand("purple");

        sut.CurrentBrand.Id.Should().Be("electric");
        sut.Theme.Should().BeSameAs(before);
        raised.Should().BeFalse();
    }

    [Fact]
    public void SetBrand_SameBrand_DoesNotRaiseOnChange()
    {
        var sut = new ThemeState();
        var raised = false;
        sut.OnChange += () => raised = true;

        sut.SetBrand("electric");

        raised.Should().BeFalse();
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

    [Theory]
    [InlineData("lime", "#163300")]
    [InlineData("sage", "#03363D")]
    [InlineData("solar", "#141D38")]
    [InlineData("mint", "#000000")]
    public void LightPrimaryBrands_HaveDarkContrastText(string brandId, string expectedContrast)
    {
        var brand = ThemeState.Brands.Single(b => b.Id == brandId);

        brand.PrimaryContrastText.Should().Be(expectedContrast);
    }

    [Theory]
    [InlineData("lime", "#163300")]
    [InlineData("sage", "#03363D")]
    [InlineData("solar", "#141D38")]
    [InlineData("mint", "#000000")]
    public void LightPrimaryBrands_HaveDarkPrimaryText(string brandId, string expectedPrimaryText)
    {
        var brand = ThemeState.Brands.Single(b => b.Id == brandId);

        brand.PrimaryText.Should().Be(expectedPrimaryText);
    }

    [Theory]
    [InlineData("electric")]
    [InlineData("royal")]
    public void DarkPrimaryBrands_UseOwnPrimaryAsPrimaryText(string brandId)
    {
        var brand = ThemeState.Brands.Single(b => b.Id == brandId);

        brand.PrimaryText.Should().Be(brand.Primary);
    }

    [Theory]
    [InlineData("electric")]
    [InlineData("lime")]
    [InlineData("sage")]
    [InlineData("royal")]
    [InlineData("solar")]
    [InlineData("mint")]
    public void PrimaryText_MeetsContrastRatioAgainstWhite(string brandId)
    {
        var brand = ThemeState.Brands.Single(b => b.Id == brandId);

        ContrastRatioAgainstWhite(brand.PrimaryText).Should().BeGreaterThanOrEqualTo(4.5,
            $"{brand.Name}'s PrimaryText must stay legible as brand-colored text on light surfaces");
    }

    private static double ContrastRatioAgainstWhite(string hex)
    {
        var luminance = RelativeLuminance(hex);
        return 1.05 / (luminance + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;

        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static double Linearize(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
