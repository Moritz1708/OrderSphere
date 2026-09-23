using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Api;
using OrderSphere.Payment.Application.Abstractions;
using OrderSphere.Payment.Domain.Entities;
using OrderSphere.Payment.Domain.Enums;
using StripeConfiguration = Stripe.StripeConfiguration;
using Xunit;

namespace OrderSphere.IntegrationTests.Api;

/// <summary>
/// The Stripe webhook reconciles <c>PaymentRecord</c>s from signed Stripe events. It must find the
/// record by the <c>orderId</c> metadata or the intent id, ask Stripe to retry (503) while the worker
/// has not yet stored a record for one of our intents, ignore foreign intents, and never move a
/// record through an invalid transition.
/// </summary>
public sealed class StripeWebhookTests : IClassFixture<PaymentApiFactory>
{
    private const string Secret = "whsec_test_secret";
    private const string Path = "api/v1/payments/webhooks/stripe";

    private readonly WebApplicationFactory<ApiMarker> _factory;

    public StripeWebhookTests(PaymentApiFactory factory) =>
        _factory = factory.WithWebHostBuilder(b => b.UseSetting("Stripe:WebhookSecret", Secret));

    [Fact]
    public async Task Unsigned_request_is_rejected()
    {
        var response = await _factory.CreateClient().PostAsync(Path,
            new StringContent(PaymentIntentEvent("payment_intent.succeeded", "pi_x", null), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Intent_of_ours_without_record_yet_asks_Stripe_to_retry()
    {
        var response = await PostSignedAsync(PaymentIntentEvent("payment_intent.succeeded", "pi_new", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Foreign_intent_is_acknowledged_and_ignored()
    {
        var response = await PostSignedAsync(PaymentIntentEvent("payment_intent.succeeded", "pi_foreign", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Capture_notice_for_failed_payment_does_not_resurrect_it()
    {
        var orderId = await SeedAsync(r => { r.MarkAuthorized("pi_failed"); r.MarkFailed("Capture rejected."); });

        var response = await PostSignedAsync(PaymentIntentEvent("payment_intent.succeeded", "pi_failed", orderId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(orderId)).Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task Charge_refund_is_matched_by_intent_id_and_marks_the_payment_refunded()
    {
        var orderId = await SeedAsync(r => r.MarkCaptured("pi_refund"));

        var response = await PostSignedAsync(ChargeRefundedEvent("pi_refund"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(orderId)).Should().Be(PaymentStatus.Refunded);
    }

    private async Task<Guid> SeedAsync(Action<PaymentRecord> arrange)
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IPaymentDbContext>();
        var record = new PaymentRecord(OrderId.From(orderId), 49.99m, "EUR", "CreditCard", "payer@example.com", Guid.NewGuid());
        arrange(record);
        context.Payments.Add(record);
        await context.SaveChangesAsync(CancellationToken.None);
        return orderId;
    }

    private async Task<PaymentStatus> StatusOfAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IPaymentDbContext>();
        return (await context.Payments.AsNoTracking().SingleAsync(p => p.OrderId == OrderId.From(orderId))).Status;
    }

    private Task<HttpResponseMessage> PostSignedAsync(string json)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{json}")));

        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
        return _factory.CreateClient().SendAsync(request);
    }

    private static string PaymentIntentEvent(string type, string intentId, Guid? orderId) =>
        Envelope(type, new Dictionary<string, object?>
        {
            ["id"] = intentId,
            ["object"] = "payment_intent",
            ["status"] = type == "payment_intent.succeeded" ? "succeeded" : "requires_payment_method",
            ["metadata"] = orderId is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["orderId"] = orderId.Value.ToString() }
        });

    private static string ChargeRefundedEvent(string intentId) =>
        Envelope("charge.refunded", new Dictionary<string, object?>
        {
            ["id"] = "ch_1",
            ["object"] = "charge",
            ["payment_intent"] = intentId,
            ["refunded"] = true
        });

    private static string Envelope(string type, Dictionary<string, object?> dataObject) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = $"evt_{Guid.NewGuid():N}",
            ["object"] = "event",
            ["api_version"] = StripeConfiguration.ApiVersion,
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["livemode"] = false,
            ["pending_webhooks"] = 1,
            ["type"] = type,
            ["data"] = new Dictionary<string, object?> { ["object"] = dataObject }
        });
}
