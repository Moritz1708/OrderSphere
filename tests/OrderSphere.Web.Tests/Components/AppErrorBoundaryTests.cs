using Bunit;
using OrderSphere.Web.Components;

namespace OrderSphere.Web.Tests.Components;

public sealed class AppErrorBoundaryTests : BunitBase
{
    [Fact]
    public void RendersChildContent_WhenNoError()
    {
        var cut = Render<AppErrorBoundary>(
            parameters => parameters.AddChildContent("<p>child works</p>"));

        cut.Markup.Should().Contain("child works");
    }

    [Fact]
    public void ShowsLocalizedFallback_WhenChildThrows()
    {
        var cut = Render<AppErrorBoundary>(
            parameters => parameters.AddChildContent<ThrowingChild>());

        // The pass-through localizer renders keys, so the fallback is recognisable by its key.
        cut.Markup.Should().Contain("Error.BoundaryTitle");
        cut.Markup.Should().Contain("Common.Retry");
        cut.Markup.Should().NotContain("child works");
    }

    private sealed class ThrowingChild : Microsoft.AspNetCore.Components.ComponentBase
    {
        protected override void OnParametersSet() => throw new InvalidOperationException("boom");
    }
}
