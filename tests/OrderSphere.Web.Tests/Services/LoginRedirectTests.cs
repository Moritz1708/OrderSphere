using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.Web.Services;

namespace OrderSphere.Web.Tests.Services;

/// <summary>
/// The BFF accepts only local paths as <c>returnUrl</c>; an absolute URI would be
/// replaced with the start page. The client must therefore send the current page as
/// a path relative to the origin.
/// </summary>
public sealed class LoginRedirectTests : BunitContext
{
    [Fact]
    public void Go_SendsTheCurrentPageAsLocalPath()
    {
        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("/products/shoe?size=42");

        LoginRedirect.Go(navigation);

        var entry = navigation.History.First();
        entry.Uri.Should().EndWith("/bff/login?returnUrl=%2Fproducts%2Fshoe%3Fsize%3D42");
        entry.Options.ForceLoad.Should().BeTrue("the BFF login endpoint lives outside the Blazor router");
    }

    [Fact]
    public void Url_EscapesTheReturnPath()
    {
        LoginRedirect.Url("/a b").Should().Be("/bff/login?returnUrl=%2Fa%20b");
    }
}
