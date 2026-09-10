using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

/// <summary>Empty, error and status presentation components.</summary>
public sealed class OsStatesTests : BunitBase
{
    [Fact]
    public void EmptyState_RendersTitleBodyAndAction()
    {
        var cut = Render<OsEmptyState>(p => p
            .Add(x => x.Title, "Nothing here")
            .Add(x => x.Body, "Add something")
            .Add(x => x.ActionText, "Go shopping")
            .Add(x => x.ActionHref, "/shop"));

        cut.Find(".os-empty__title").TextContent.Should().Be("Nothing here");
        cut.Find(".os-empty__body").TextContent.Should().Be("Add something");
        cut.Find("a.os-btn").GetAttribute("href").Should().Be("/shop");
        cut.Find("a.os-btn").TextContent.Trim().Should().Be("Go shopping");
    }

    [Fact]
    public void EmptyState_WithoutAction_RendersNoButton()
    {
        var cut = Render<OsEmptyState>(p => p.Add(x => x.Title, "Nothing"));

        cut.FindAll(".os-btn").Should().BeEmpty();
        cut.FindAll(".os-empty__actions").Should().BeEmpty();
    }

    [Fact]
    public void EmptyState_Compact_AddsModifier()
    {
        var cut = Render<OsEmptyState>(p => p.Add(x => x.Title, "Nothing").Add(x => x.Compact, true));

        cut.Find(".os-empty").ClassList.Should().Contain("os-empty--compact");
    }

    [Fact]
    public void ErrorState_IsAnAlert_WithRetry()
    {
        var retried = false;
        var cut = Render<OsErrorState>(p => p
            .Add(x => x.Message, "It broke")
            .Add(x => x.OnRetry, EventCallback.Factory.Create<MouseEventArgs>(this, () => retried = true)));

        cut.Find(".os-error").GetAttribute("role").Should().Be("alert");
        cut.Find(".os-error__message").TextContent.Should().Be("It broke");
        cut.Find("button.os-btn").TextContent.Should().Contain("Common.Retry");

        cut.Find("button.os-btn").Click();

        retried.Should().BeTrue();
    }

    [Fact]
    public void ErrorState_WithoutRetry_HasNoButton()
    {
        var cut = Render<OsErrorState>(p => p.Add(x => x.Message, "It broke"));

        cut.FindAll("button").Should().BeEmpty();
    }

    [Theory]
    [InlineData(StatusTone.Neutral, "os-chip--neutral")]
    [InlineData(StatusTone.Success, "os-chip--success")]
    [InlineData(StatusTone.Danger, "os-chip--danger")]
    [InlineData(StatusTone.Accent, "os-chip--accent")]
    public void StatusChip_CarriesToneClass(StatusTone tone, string expectedClass)
    {
        var cut = Render<OsStatusChip>(p => p.Add(x => x.Tone, tone).Add(x => x.Text, "Shipped"));

        var chip = cut.Find(".os-chip");
        chip.ClassList.Should().Contain(expectedClass);
        chip.GetAttribute("role").Should().Be("status");
        chip.TextContent.Trim().Should().Be("Shipped");
    }

    [Fact]
    public void StatusChip_Dot_IsDecorative()
    {
        var cut = Render<OsStatusChip>(p => p.Add(x => x.Text, "x").Add(x => x.Dot, true));

        cut.Find(".os-chip__dot").GetAttribute("aria-hidden").Should().Be("true");
    }
}
