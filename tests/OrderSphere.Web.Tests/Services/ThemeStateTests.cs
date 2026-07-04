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
}
