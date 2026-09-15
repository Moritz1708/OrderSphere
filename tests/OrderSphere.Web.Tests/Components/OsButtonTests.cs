using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

public sealed class OsButtonTests : BunitBase
{
    [Fact]
    public void WithoutHref_RendersAButton()
    {
        var cut = Render<OsButton>(p => p.AddChildContent("Go"));

        var button = cut.Find("button.os-btn");
        button.GetAttribute("type").Should().Be("button");
        button.ClassList.Should().Contain("os-btn--primary").And.Contain("os-btn--md");
        button.TextContent.Trim().Should().Be("Go");
    }

    [Fact]
    public void WithHref_RendersAnAnchor()
    {
        var cut = Render<OsButton>(p => p.Add(x => x.Href, "/shop").AddChildContent("Shop"));

        var anchor = cut.Find("a.os-btn");
        anchor.GetAttribute("href").Should().Be("/shop");
        cut.FindAll("button").Should().BeEmpty();
    }

    [Theory]
    [InlineData(OsButton.ButtonVariant.Secondary, "os-btn--secondary")]
    [InlineData(OsButton.ButtonVariant.Ghost, "os-btn--ghost")]
    [InlineData(OsButton.ButtonVariant.Danger, "os-btn--danger")]
    public void Variant_SetsTheModifierClass(OsButton.ButtonVariant variant, string expectedClass)
    {
        var cut = Render<OsButton>(p => p.Add(x => x.Variant, variant));

        cut.Find(".os-btn").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void Loading_DisablesTheButton_AndAnnouncesBusy()
    {
        var cut = Render<OsButton>(p => p.Add(x => x.Loading, true).AddChildContent("Save"));

        var button = cut.Find("button");
        button.HasAttribute("disabled").Should().BeTrue();
        button.GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(".os-btn__spinner").Should().HaveCount(1);
        button.ClassList.Should().Contain("is-loading");
    }

    [Fact]
    public void DisabledAnchor_IsNotNavigable()
    {
        // An <a> cannot carry disabled; the link must drop its href and leave the tab order.
        var cut = Render<OsButton>(p => p.Add(x => x.Href, "/shop").Add(x => x.Disabled, true));

        var anchor = cut.Find("a");
        anchor.HasAttribute("href").Should().BeFalse();
        anchor.GetAttribute("aria-disabled").Should().Be("true");
        anchor.GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void Click_InvokesOnClick()
    {
        var clicked = false;
        var cut = Render<OsButton>(p =>
            p.Add(x => x.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, () => clicked = true)));

        cut.Find("button").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void FullWidth_AndIcons_Render()
    {
        var cut = Render<OsButton>(p => p
            .Add(x => x.FullWidth, true)
            .Add(x => x.StartIcon, Icons.Material.Outlined.Add)
            .Add(x => x.EndIcon, Icons.Material.Outlined.ArrowForward));

        cut.Find(".os-btn").ClassList.Should().Contain("os-btn--full");
        cut.FindAll(".os-btn__icon").Should().HaveCount(2);
    }
}
