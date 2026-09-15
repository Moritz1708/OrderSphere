using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace OrderSphere.Bff.Tests;

/// <summary>
/// The BFF applies <c>BffUserPolicy</c> to the whole <c>/api/**</c> proxy surface, with
/// one deliberate exception: catalog reads are anonymous so the storefront can be
/// browsed without signing in. These tests pin that boundary on the BFF hop.
/// <para>
/// No gateway runs behind the test host, so a request that clears authorization ends
/// as a proxy error rather than a payload. The assertion is therefore only about what
/// the BFF decides itself: whether it answers 401 before proxying.
/// </para>
/// </summary>
public sealed class PublicCatalogRoutesTests(BffWebApplicationFactory factory)
    : IClassFixture<BffWebApplicationFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    [Theory]
    [InlineData("/api/v1/products")]
    [InlineData("/api/v1/products?page=1&pageSize=12")]
    [InlineData("/api/v1/products/some-slug")]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1/brands")]
    [InlineData("/api/v1/reviews/product/00000000-0000-0000-0000-000000000001")]
    public async Task CatalogReads_AreNotRejectedForAnonymousUsers(string path)
    {
        var response = await Client().GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "catalog reads are public")
            .And.NotBe(HttpStatusCode.Redirect)
            .And.NotBe(HttpStatusCode.Found);
    }

    [Theory]
    [InlineData("/api/v1/cart")]
    [InlineData("/api/v1/orders")]
    [InlineData("/api/v1/profile")]
    [InlineData("/api/v1/admin/products")]
    public async Task OtherApiReads_StillRequireASession(string path)
    {
        var response = await Client().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/v1/products/00000000-0000-0000-0000-000000000001/stock/decrement")]
    [InlineData("/api/v1/reviews/product/00000000-0000-0000-0000-000000000001")]
    public async Task CatalogWrites_StillRequireASession(string path)
    {
        // The anonymous routes are GET/HEAD only; a POST falls through to the
        // authenticated catch-all, where either authorization or CSRF stops it.
        var response = await Client().PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
