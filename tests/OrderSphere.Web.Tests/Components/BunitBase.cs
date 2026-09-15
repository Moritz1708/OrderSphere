using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using OrderSphere.Web.Services;

namespace OrderSphere.Web.Tests.Components;

/// <summary>
/// Shared base for bUnit component tests. Registers MudBlazor services and a
/// pass-through string localizer so components render without cultural infrastructure.
/// Localized strings resolve to their key, enabling stable markup assertions.
/// </summary>
public abstract class BunitBase : BunitContext, IAsyncLifetime
{
    protected BunitBase()
    {
        Services.AddMudServices();
        Services.AddSingleton<IStringLocalizer<AppStrings>>(new PassthroughLocalizer());
        Services.AddScoped<IMotionService, MotionService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    // MudBlazor 9.x registers services (PointerEventsNoneService, KeyInterceptorService)
    // that are IAsyncDisposable-only. BunitContext.DisposeAsync() internally calls
    // Dispose(bool), which in turn calls ServiceProvider.Dispose() — that throws
    // InvalidOperationException for any IAsyncDisposable-only service that was
    // instantiated during the test. Catch it here so the test does not fail on teardown.
    async Task IAsyncLifetime.DisposeAsync()
    {
        try { await DisposeAsync(); }
        catch (InvalidOperationException) { }
    }

    private sealed class PassthroughLocalizer : IStringLocalizer<AppStrings>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
