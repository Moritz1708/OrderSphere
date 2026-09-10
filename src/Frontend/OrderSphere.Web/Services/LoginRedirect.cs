using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace OrderSphere.Web.Services;

/// <summary>
/// Sends the browser to the BFF's login endpoint. Blazor's router intercepts
/// same-origin anchors and would render the 404 page for /bff/login, so every
/// sign-in control navigates with a full load instead of an href.
/// </summary>
public static class LoginRedirect
{
    public static string Url(string returnUrl) => $"/bff/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

    /// <summary>Full-page navigation to login, returning to the current page afterwards.</summary>
    public static void Go(NavigationManager navigation) => navigation.NavigateTo(Url(navigation.Uri), forceLoad: true);

    /// <summary>True when signed in; otherwise redirects to login and returns false.</summary>
    public static async Task<bool> EnsureSignedInAsync(Task<AuthenticationState>? authState, NavigationManager navigation)
    {
        var state = await (authState ?? Task.FromResult(new AuthenticationState(new ClaimsPrincipal())));
        if (state.User.Identity?.IsAuthenticated == true)
            return true;

        Go(navigation);
        return false;
    }
}
