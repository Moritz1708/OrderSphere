using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace OrderSphere.Web.Services;

/// <summary>
/// Blazor-side wrapper around <c>wwwroot/js/motion.js</c>: scroll reveals, the
/// theme attribute, scroll locking and the reduced-motion query.
/// </summary>
public interface IMotionService
{
    /// <summary>Marks the document ready for motion. Called once per layout.</summary>
    ValueTask InitAsync();

    /// <summary>Reveals an element (or, for a group, its children) on scroll.</summary>
    ValueTask ObserveAsync(ElementReference element);

    ValueTask UnobserveAsync(ElementReference element);

    /// <summary>Applies a theme choice to <c>html[data-theme]</c> and persists it.</summary>
    ValueTask SetThemeAsync(bool isDark);

    ValueTask LockScrollAsync(bool locked);

    ValueTask<bool> PrefersReducedMotionAsync();
}

/// <inheritdoc />
/// <remarks>
/// Every call is a no-op when the module could not be loaded, so component tests
/// under bUnit's loose JS interop need no JavaScript setup, and a prerendered or
/// script-blocked page degrades to no animation rather than an exception.
/// </remarks>
public sealed class MotionService(IJSRuntime js) : IMotionService, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _loadFailed;

    public async ValueTask InitAsync() => await InvokeAsync("init");

    public async ValueTask ObserveAsync(ElementReference element) =>
        await InvokeAsync("observeReveals", element);

    public async ValueTask UnobserveAsync(ElementReference element) =>
        await InvokeAsync("unobserve", element);

    public async ValueTask SetThemeAsync(bool isDark) => await InvokeAsync("setTheme", isDark);

    public async ValueTask LockScrollAsync(bool locked) => await InvokeAsync("lockScroll", locked);

    public async ValueTask<bool> PrefersReducedMotionAsync()
    {
        var module = await GetModuleAsync();
        if (module is null)
            return false;

        try
        {
            return await module.InvokeAsync<bool>("prefersReducedMotion");
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async ValueTask InvokeAsync(string identifier, params object?[] args)
    {
        var module = await GetModuleAsync();
        if (module is null)
            return;

        try
        {
            await module.InvokeVoidAsync(identifier, args);
        }
        catch (JSException)
        {
            // The page is gone or the call raced a navigation. Motion is decorative.
        }
        catch (InvalidOperationException)
        {
            // Interop is unavailable (prerender, disposed circuit).
        }
    }

    private async ValueTask<IJSObjectReference?> GetModuleAsync()
    {
        if (_module is not null || _loadFailed)
            return _module;

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/motion.js");
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Loose JS interop in tests returns null, and a blocked script should
            // not break rendering. Remember the failure so we retry only once.
            _loadFailed = true;
        }

        return _module;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;

        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // The circuit is already gone.
        }
    }
}
