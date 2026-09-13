using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

// MudBlazor 9 no longer wires a custom ActivatorContent; each activator has to call
// the MenuContext itself. These tests keep both header menus from going dead again.
public sealed class HeaderMenuTests : BunitBase
{
    public HeaderMenuTests() => Services.AddSingleton(new HttpClient());

    [Fact]
    public void LocaleMenu_OpensOnActivatorClick()
    {
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<LocaleMenu>();

        cut.Find("button.os-iconbtn").Click();

        popovers.WaitForAssertion(() => popovers.Markup.Should().Contain("Currency.Label"));
        cut.Find("button.os-iconbtn").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void AccountMenu_OpensOnActivatorClick()
    {
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<AccountMenu>(p => p.Add(x => x.Name, "Ada"));

        cut.Find("button.os-iconbtn").Click();

        popovers.WaitForAssertion(() => popovers.Markup.Should().Contain("Account.MyOrders").And.Contain("Ada"));
        cut.Find("button.os-iconbtn").GetAttribute("aria-haspopup").Should().Be("menu");
    }
}
