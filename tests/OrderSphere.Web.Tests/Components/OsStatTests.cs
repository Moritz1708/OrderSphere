using Bunit;
using OrderSphere.Web.Components.Ui;

namespace OrderSphere.Web.Tests.Components;

public sealed class OsStatTests : BunitBase
{
    [Fact]
    public void WithoutCountUp_RendersTheFinalValueImmediately()
    {
        var cut = Render<OsStat>(p => p
            .Add(x => x.Label, "Orders")
            .Add(x => x.Value, 1234m)
            .Add(x => x.CountUp, false));

        cut.Find(".os-stat__value").TextContent.Should().Be(1234m.ToString("N0"));
        cut.Markup.Should().Contain("Orders");
    }

    [Fact]
    public void WithCountUp_SettlesOnTheFinalValue()
    {
        var cut = Render<OsStat>(p => p
            .Add(x => x.Label, "Revenue")
            .Add(x => x.Value, 500m)
            .Add(x => x.Format, v => v.ToString("0.00")));

        // The decimal separator follows the test host's culture; compute the expectation the same way.
        cut.WaitForAssertion(
            () => cut.Find(".os-stat__value").TextContent.Should().Be(500m.ToString("0.00")),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Delta_RendersWithItsTone()
    {
        var cut = Render<OsStat>(p => p
            .Add(x => x.Label, "Today")
            .Add(x => x.Value, 3m)
            .Add(x => x.CountUp, false)
            .Add(x => x.Delta, "+2")
            .Add(x => x.DeltaTone, StatusTone.Success));

        var delta = cut.Find(".os-stat__delta");
        delta.TextContent.Should().Be("+2");
        delta.ClassList.Should().Contain("os-stat__delta--success");
    }

    [Fact]
    public void Href_RendersAsALink()
    {
        var cut = Render<OsStat>(p => p
            .Add(x => x.Label, "Low stock")
            .Add(x => x.Value, 2m)
            .Add(x => x.CountUp, false)
            .Add(x => x.Href, "/admin/products"));

        cut.Find("a.os-stat").GetAttribute("href").Should().Be("/admin/products");
    }
}
