using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

public sealed class PageTransitionTests : BunitBase
{
    [Fact]
    public void WrapsContent_InTheAnimatedPageElement()
    {
        var cut = Render<PageTransition>(p => p.AddChildContent("<p>content</p>"));

        cut.Find(".os-page").InnerHtml.Should().Contain("content");
    }

    [Fact]
    public void PathChange_ReRenders_ButQueryChangeDoesNot()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<PageTransition>(p => p.AddChildContent("<p>content</p>"));

        var initial = cut.RenderCount;

        nav.NavigateTo("/shop");
        cut.WaitForAssertion(() => cut.RenderCount.Should().Be(initial + 1));

        // /search?q=… must update in place; a remount would throw away the page's state.
        nav.NavigateTo("/shop?page=2");
        cut.RenderCount.Should().Be(initial + 1);

        nav.NavigateTo("/categories");
        cut.WaitForAssertion(() => cut.RenderCount.Should().Be(initial + 2));
    }
}
