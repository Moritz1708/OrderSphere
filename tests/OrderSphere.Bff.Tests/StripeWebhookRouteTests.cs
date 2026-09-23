using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace OrderSphere.Bff.Tests;

/// <summary>
/// Stripe delivers webhooks without a session or antiforgery token; the request is
/// authenticated downstream by its <c>Stripe-Signature</c>. The BFF therefore exposes
/// exactly one anonymous POST route outside <c>/api</c>, where neither
/// <c>BffUserPolicy</c> nor the CSRF middleware applies.
/// <para>
/// No gateway runs behind the test host, so a request that clears the BFF ends as a
/// proxy error rather than a payload (see <see cref="PublicCatalogRoutesTests"/>).
/// </para>
/// </summary>
public sealed class StripeWebhookRouteTests(BffWebApplicationFactory factory)
    : IClassFixture<BffWebApplicationFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static StringContent StripePayload() =>
        new("{\"id\":\"evt_test\"}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task AnonymousPost_ToStripeWebhook_IsProxiedWithoutSessionOrCsrfToken()
    {
        var response = await Client().PostAsync("/webhooks/stripe", StripePayload());

        // Without the route the request falls through to the SPA fallback (404 for POST);
        // with it, YARP forwards and fails on the missing gateway with a 5xx proxy error.
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500,
            "the request must reach the proxy instead of being rejected or unrouted");
    }

    [Fact]
    public async Task AnonymousPost_ToPaymentApi_StillRequiresASession()
    {
        var response = await Client().PostAsync("/api/v1/payments/webhooks/stripe", StripePayload());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
