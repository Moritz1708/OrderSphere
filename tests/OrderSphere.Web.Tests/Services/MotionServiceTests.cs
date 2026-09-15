using Bunit;
using Microsoft.AspNetCore.Components;

namespace OrderSphere.Web.Tests.Services;

/// <summary>
/// Under bUnit's loose JS interop the module import yields no object. Every call
/// must then be a silent no-op — that is what lets any kit component render in a
/// test without JavaScript, and what keeps a script-blocked page from throwing.
/// </summary>
public sealed class MotionServiceTests : BunitContext
{
    public MotionServiceTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public async Task AllCalls_AreNoOps_WhenTheModuleIsUnavailable()
    {
        var sut = new MotionService(JSInterop.JSRuntime);

        await sut.InitAsync();
        await sut.SetThemeAsync(true);
        await sut.LockScrollAsync(true);
        await sut.ObserveAsync(default(ElementReference));
        await sut.UnobserveAsync(default(ElementReference));
        await sut.FocusFirstInvalidAsync();
        var reduced = await sut.PrefersReducedMotionAsync();

        reduced.Should().BeFalse("the safe default is to animate");
        await sut.DisposeAsync();
    }
}
