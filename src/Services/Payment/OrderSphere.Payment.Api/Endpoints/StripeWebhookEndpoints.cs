using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Api.Logging;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;
using Stripe;

namespace OrderSphere.Payment.Api.Endpoints;

/// <summary>
/// Inbound Stripe webhook. Provides asynchronous capture/refund confirmation: Stripe is the
/// source of truth for settlement, so these events reconcile the local <c>PaymentRecord</c>
/// with the provider. The endpoint is anonymous (authenticated by Stripe signature) and
/// idempotent — a re-delivered event is a no-op once the record reached the target state.
/// <para>
/// Only valid status transitions are applied. An event that is merely out of date (a failure
/// notice for a payment that later succeeded) is ignored; one that contradicts the record
/// (a capture for a payment recorded as failed, a different intent) is logged as
/// <see cref="PaymentApiLog.StripeReconciliationRequired"/> for an operator.
/// </para>
/// </summary>
public static class StripeWebhookEndpoints
{
    private const string PaymentIntentSucceeded = "payment_intent.succeeded";
    private const string PaymentIntentPaymentFailed = "payment_intent.payment_failed";
    private const string ChargeRefunded = "charge.refunded";

    public static void MapStripeWebhookEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/payments/webhooks/stripe", async (
            HttpRequest request,
            PaymentDbContext context,
            IOptions<StripeOptions> stripeOptions,
            ILogger<StripeWebhookMarker> logger,
            CancellationToken ct) =>
        {
            var secret = stripeOptions.Value.WebhookSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                logger.LogWarning("Stripe webhook received but no WebhookSecret is configured. Ignoring.");
                return Results.Ok();
            }

            // The route is anonymous; without a signature header the SDK does not raise a
            // StripeException, so reject it here instead of surfacing a 500.
            var signature = request.Headers["Stripe-Signature"].ToString();
            if (string.IsNullOrEmpty(signature))
            {
                logger.LogWarning("Stripe webhook rejected: Stripe-Signature header missing.");
                return Results.BadRequest();
            }

            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync(ct);

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, signature, secret);
            }
            catch (StripeException ex)
            {
                logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return Results.BadRequest();
            }

            if (stripeEvent.Type is not (PaymentIntentSucceeded or PaymentIntentPaymentFailed or ChargeRefunded))
            {
                logger.LogDebug("Stripe webhook {Type} ignored (not reconciled).", stripeEvent.Type);
                return Results.Ok();
            }

            // payment_intent.* carry the PaymentIntent with the metadata the provider set;
            // charge.refunded carries a Charge that references the originating PaymentIntent.
            var (paymentIntentId, orderId) = stripeEvent.Data.Object switch
            {
                PaymentIntent intent => (intent.Id, ReadOrderId(intent)),
                Charge charge => (charge.PaymentIntentId, (Guid?)null),
                _ => (null, null)
            };

            if (paymentIntentId is null)
            {
                logger.LogInformation("Stripe webhook {Type} ignored (no PaymentIntent reference).", stripeEvent.Type);
                return Results.Ok();
            }

            return await ReconcileAsync(context, logger, stripeEvent.Type, paymentIntentId, orderId, ct);
        })
        .AllowAnonymous();
    }

    private static async Task<IResult> ReconcileAsync(
        PaymentDbContext context,
        ILogger logger,
        string eventType,
        string paymentIntentId,
        Guid? orderId,
        CancellationToken ct)
    {
        var record = await FindRecordAsync(context, paymentIntentId, orderId, ct);
        if (record is null)
        {
            if (orderId is null)
            {
                logger.LogInformation("Stripe webhook {Type} for intent {IntentId} matches no payment; ignored.",
                    eventType, paymentIntentId);
                return Results.Ok();
            }

            // Created by the payment worker, which stores the record only once the payment has run.
            // A non-2xx makes Stripe redeliver later, when the record exists.
            logger.LogWarning("Stripe webhook {Type} for order {OrderId} arrived before its payment record; Stripe will retry.",
                eventType, orderId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (record.TransactionId is not null && record.TransactionId != paymentIntentId)
        {
            logger.StripeReconciliationRequired(eventType, paymentIntentId, record.OrderId.Value, record.Status, record.TransactionId);
            return Results.Ok();
        }

        // Stripe calls anonymously; act in the record's tenant so audit rows are stamped with it.
        using var tenantScope = AmbientTenantContext.BeginScope(record.TenantId);

        var transition = eventType switch
        {
            PaymentIntentSucceeded => record.MarkCaptured(paymentIntentId),
            PaymentIntentPaymentFailed => record.MarkFailed("Stripe reported payment failure."),
            _ => record.MarkRefunded()
        };

        if (transition.IsFailure)
        {
            if (eventType == PaymentIntentSucceeded && record.Status == PaymentStatus.Failed)
                logger.StripeReconciliationRequired(eventType, paymentIntentId, record.OrderId.Value, record.Status, record.TransactionId);
            else
                logger.LogDebug("Stripe webhook {Type} for intent {IntentId} is out of date; payment is {Status}.",
                    eventType, paymentIntentId, record.Status);
            return Results.Ok();
        }

        if (!context.ChangeTracker.HasChanges())
        {
            logger.LogDebug("Stripe webhook {Type} for intent {IntentId} already reconciled.", eventType, paymentIntentId);
            return Results.Ok();
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Stripe webhook {Type} reconciled the payment for order {OrderId}.",
            eventType, record.OrderId);
        return Results.Ok();
    }

    /// <summary>
    /// Finds the record by the <c>orderId</c> metadata (unique), falling back to the intent id.
    /// The request carries no tenant, so the tenant query filter is bypassed; soft-deleted rows
    /// stay excluded explicitly because <c>IgnoreQueryFilters</c> drops that filter too.
    /// </summary>
    private static async Task<Domain.Entities.PaymentRecord?> FindRecordAsync(
        PaymentDbContext context, string paymentIntentId, Guid? orderId, CancellationToken ct)
    {
        var payments = context.Payments.IgnoreQueryFilters().Where(p => !p.IsDeleted);

        if (orderId is { } id
            && await payments.FirstOrDefaultAsync(p => p.OrderId == OrderId.From(id), ct) is { } byOrder)
        {
            return byOrder;
        }

        return await payments.FirstOrDefaultAsync(p => p.TransactionId == paymentIntentId, ct);
    }

    private static Guid? ReadOrderId(PaymentIntent intent) =>
        intent.Metadata is not null
        && intent.Metadata.TryGetValue("orderId", out var raw)
        && Guid.TryParse(raw, out var orderId)
            ? orderId
            : null;

    /// <summary>Logger category marker for the webhook endpoint.</summary>
    public sealed class StripeWebhookMarker;
}
