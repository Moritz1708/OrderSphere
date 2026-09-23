using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderSphere.Payment.Infrastructure;
using OrderSphere.Payment.Infrastructure.Providers;
using Stripe;
using Xunit;

namespace OrderSphere.Payment.Tests;

/// <summary>
/// The Stripe provider registers under the "CreditCard" method name so existing checkout routes to
/// it without a UI contract change, but only when an API key is configured. Every Stripe call must
/// carry a deterministic idempotency key, and only declines/invalid requests may come back as a
/// <c>Result</c> failure — transient faults must propagate so Service Bus redelivers.
/// </summary>
public sealed class StripePaymentProviderTests
{
    private static readonly Guid OrderId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static PaymentRequest Request() =>
        new(OrderId, 49.99m, "EUR", "customer@example.com", Guid.Empty, Guid.NewGuid());

    private static StripePaymentProvider Provider(FakeStripeClient client) =>
        new(client, NullLogger<StripePaymentProvider>.Instance);

    private static StripeException StripeError(HttpStatusCode status, string type, string? code = null) =>
        new(status, new StripeError { Type = type, Code = code }, "stripe error");

    [Fact]
    public void MethodName_IsCreditCard()
    {
        var provider = new StripePaymentProvider(
            Substitute.For<IStripeClient>(),
            NullLogger<StripePaymentProvider>.Instance);

        provider.MethodName.Should().Be("CreditCard");
    }

    [Fact]
    public async Task Authorize_UsesOrderScopedIdempotencyKey_AndTagsTheIntent()
    {
        var client = new FakeStripeClient();
        var request = Request();

        var result = await Provider(client).AuthorizeAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionId.Should().Be("pi_1");
        var call = client.Calls.Single();
        call.Path.Should().Be("/v1/payment_intents");
        call.RequestOptions!.IdempotencyKey.Should().Be($"os-pi-create-{OrderId:N}");
        var metadata = ((PaymentIntentCreateOptions)call.Options).Metadata;
        metadata["orderId"].Should().Be(OrderId.ToString());
        metadata["correlationId"].Should().Be(request.CorrelationId.ToString());
        metadata.Should().ContainKey("tenantId");
    }

    [Fact]
    public async Task Authorize_WhenIntentHoldsNoFunds_CancelsItAndFails()
    {
        var client = new FakeStripeClient
        {
            Respond = (_, path) => path.EndsWith("/cancel")
                ? new PaymentIntent { Id = "pi_1", Status = "canceled" }
                : new PaymentIntent { Id = "pi_1", Status = "requires_action" }
        };

        var result = await Provider(client).AuthorizeAsync(Request());

        result.IsFailure.Should().BeTrue();
        client.Calls.Should().Contain(c => c.Path == "/v1/payment_intents/pi_1/cancel"
            && c.RequestOptions!.IdempotencyKey == "os-pi-cancel-pi_1");
    }

    [Fact]
    public async Task Authorize_CardDecline_ReturnsFailure()
    {
        var client = new FakeStripeClient
        {
            Respond = (_, _) => StripeError(HttpStatusCode.PaymentRequired, "card_error", "card_declined")
        };

        var result = await Provider(client).AuthorizeAsync(Request());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Capture_UsesIntentScopedIdempotencyKey()
    {
        var client = new FakeStripeClient { Respond = (_, _) => new PaymentIntent { Id = "pi_1", Status = "succeeded" } };

        var result = await Provider(client).CaptureAsync("pi_1", 49.99m);

        result.IsSuccess.Should().BeTrue();
        client.Calls.Single().RequestOptions!.IdempotencyKey.Should().Be("os-pi-capture-pi_1");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "api_error")]
    [InlineData(HttpStatusCode.TooManyRequests, "invalid_request_error")]
    [InlineData(HttpStatusCode.Unauthorized, "invalid_request_error")]
    [InlineData(HttpStatusCode.Conflict, "invalid_request_error")]
    [InlineData(HttpStatusCode.BadRequest, "idempotency_error")]
    public async Task Capture_NonPermanentStripeError_Propagates(HttpStatusCode status, string type)
    {
        var client = new FakeStripeClient { Respond = (_, _) => StripeError(status, type) };

        var act = () => Provider(client).CaptureAsync("pi_1", 49.99m);

        await act.Should().ThrowAsync<StripeException>();
    }

    [Fact]
    public async Task Capture_NetworkFailure_Propagates()
    {
        var client = new FakeStripeClient { Respond = (_, _) => new HttpRequestException("connection reset") };

        var act = () => Provider(client).CaptureAsync("pi_1", 49.99m);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Capture_Decline_ReturnsFailure()
    {
        var client = new FakeStripeClient
        {
            Respond = (_, _) => StripeError(HttpStatusCode.PaymentRequired, "card_error", "card_declined")
        };

        var result = await Provider(client).CaptureAsync("pi_1", 49.99m);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Capture_OfAlreadyCapturedIntent_ReturnsSuccess()
    {
        // An earlier attempt captured the intent but its idempotency key has expired.
        var client = new FakeStripeClient
        {
            Respond = (method, _) => method == HttpMethod.Get
                ? new PaymentIntent { Id = "pi_1", Status = "succeeded" }
                : StripeError(HttpStatusCode.BadRequest, "invalid_request_error", "payment_intent_unexpected_state")
        };

        var result = await Provider(client).CaptureAsync("pi_1", 49.99m);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Refund_IsKeyedPerIntentAndReference()
    {
        var client = new FakeStripeClient { Respond = (_, _) => new Refund { Id = "re_1" } };

        var result = await Provider(client).RefundAsync("pi_1", 10m, "ret42");

        result.IsSuccess.Should().BeTrue();
        client.Calls.Single().RequestOptions!.IdempotencyKey.Should().Be("os-refund-pi_1-ret42");
    }

    [Fact]
    public async Task Refund_OfAlreadyRefundedCharge_ReturnsSuccess()
    {
        var client = new FakeStripeClient
        {
            Respond = (_, _) => StripeError(HttpStatusCode.BadRequest, "invalid_request_error", "charge_already_refunded")
        };

        var result = await Provider(client).RefundAsync("pi_1", 10m, "ocf");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void WhenStripeApiKeyConfigured_CreditCardResolvesToStripe()
    {
        using var sp = BuildInfrastructure(new() { ["Stripe:ApiKey"] = "sk_test_dummy" });

        var factory = sp.GetRequiredService<IPaymentProviderFactory>();

        factory.GetProvider("CreditCard").Should().BeOfType<StripePaymentProvider>();
    }

    [Fact]
    public void WhenStripeApiKeyMissing_CreditCardResolvesToSimulatedProvider()
    {
        using var sp = BuildInfrastructure(new());

        var factory = sp.GetRequiredService<IPaymentProviderFactory>();

        factory.GetProvider("CreditCard").Should().BeOfType<CreditCardPaymentProvider>();
    }

    private static ServiceProvider BuildInfrastructure(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Records every request the Stripe services issue. <see cref="Respond"/> returns the entity
    /// to deserialize into, or an exception to throw.
    /// </summary>
    private sealed class FakeStripeClient : IStripeClient
    {
        public List<(HttpMethod Method, string Path, BaseOptions Options, RequestOptions? RequestOptions)> Calls { get; } = [];

        public Func<HttpMethod, string, object> Respond { get; init; } =
            (_, _) => new PaymentIntent { Id = "pi_1", Status = "requires_capture" };

        public string ApiBase => "https://api.stripe.com";
        public string ApiKey => "sk_test_fake";
        public string ClientId => "";
        public string ConnectBase => "";
        public string FilesBase => "";
        public string MeterEventsBase => "";

        public Task<T> RequestAsync<T>(HttpMethod method, string path, BaseOptions options,
            RequestOptions requestOptions, CancellationToken cancellationToken = default)
            where T : IStripeEntity
        {
            Calls.Add((method, path, options, requestOptions));
            return Respond(method, path) switch
            {
                Exception ex => Task.FromException<T>(ex),
                var entity => Task.FromResult((T)entity)
            };
        }

        public Task<Stream> RequestStreamingAsync(HttpMethod method, string path, BaseOptions options,
            RequestOptions requestOptions, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
