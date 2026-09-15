using Bunit;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

public sealed class AddressFormTests : BunitBase
{
    private static AddressFormModel Complete() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Street = "Analytical Engine Way 1",
        PostalCode = "10115",
        City = "Berlin",
        Country = "Deutschland",
        Email = "ada@example.org",
    };

    [Fact]
    public async Task EmptyModel_FailsValidation()
    {
        var cut = Render<AddressForm>(p => p.Add(x => x.Model, new AddressFormModel()));

        var valid = await cut.InvokeAsync(() => cut.Instance.ValidateAsync());

        valid.Should().BeFalse();
        cut.Markup.Should().Contain("Validation.Required");
    }

    [Fact]
    public async Task CompleteModel_PassesValidation()
    {
        var cut = Render<AddressForm>(p => p.Add(x => x.Model, Complete()).Add(x => x.ShowEmail, true));

        var valid = await cut.InvokeAsync(() => cut.Instance.ValidateAsync());

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task GermanPostalCode_MustHaveFiveDigits()
    {
        var model = Complete();
        model.PostalCode = "1234";
        var cut = Render<AddressForm>(p => p.Add(x => x.Model, model));

        var valid = await cut.InvokeAsync(() => cut.Instance.ValidateAsync());

        valid.Should().BeFalse();
        cut.Markup.Should().Contain("Validation.PostalCode");
    }

    [Fact]
    public async Task InvalidEmail_IsRejected_WhenEmailIsShown()
    {
        var model = Complete();
        model.Email = "not-an-email";
        var cut = Render<AddressForm>(p => p.Add(x => x.Model, model).Add(x => x.ShowEmail, true));

        var valid = await cut.InvokeAsync(() => cut.Instance.ValidateAsync());

        valid.Should().BeFalse();
        cut.Markup.Should().Contain("Validation.Email");
    }

    [Fact]
    public void LabelAndEmail_AreOptInFields()
    {
        var cut = Render<AddressForm>(p => p.Add(x => x.Model, Complete()));

        cut.Markup.Should().NotContain("Address.Label").And.NotContain("Address.Email");

        var withBoth = Render<AddressForm>(p => p.Add(x => x.Model, Complete()).Add(x => x.ShowLabel, true).Add(x => x.ShowEmail, true));

        withBoth.Markup.Should().Contain("Address.Label").And.Contain("Address.Email");
    }

    [Fact]
    public void Model_MapsToTheApiContracts_Trimmed()
    {
        var model = Complete();
        model.Street = "  Somewhere 5  ";

        var checkout = model.ToCheckoutAddress();
        checkout.Street.Should().Be("Somewhere 5");
        checkout.Country.Should().Be("Deutschland");

        var create = model.ToCreateAddressRequest(label: "Home", setAsDefault: true);
        create.Label.Should().Be("Home");
        create.SetAsDefault.Should().BeTrue();
        create.City.Should().Be("Berlin");
    }

    [Fact]
    public void Model_TouchedAndComplete_TrackProgress()
    {
        var model = new AddressFormModel();
        model.IsTouched.Should().BeFalse();
        model.IsComplete.Should().BeFalse();

        model.FirstName = "Ada";
        model.IsTouched.Should().BeTrue();
        model.IsComplete.Should().BeFalse();

        var complete = Complete();
        complete.IsComplete.Should().BeTrue();

        complete.Clear();
        complete.IsTouched.Should().BeFalse();
        complete.Country.Should().Be("Deutschland");
    }
}
