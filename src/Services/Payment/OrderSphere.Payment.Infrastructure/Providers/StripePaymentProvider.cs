using System.Net;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.Payment.Domain.Errors;
using Stripe;

namespace OrderSphere.Payment.Infrastructure.Providers;

/// <summary>
/// Real payment provider backed by Stripe (test mode). Maps the Authorize → Capture → Refund flow
/// onto PaymentIntents with manual capture; <see cref="VoidAsync"/> cancels an uncaptured intent.
/// Registered under the "CreditCard" method name so existing checkout routes here without a UI
/// contract change.
/// <para>
/// Every call carries a deterministic <see cref="RequestOptions.IdempotencyKey"/> derived from the
/// order or intent. Stripe stores the first response per key for at least 24 hours and replays it,
/// so a Service Bus redelivery — including one after a timeout on a request that did succeed —
/// converges on the original intent, capture, cancellation or refund instead of repeating it.
/// </para>
/// <para>
/// Error mapping: declines and invalid requests (<c>card_error</c>, <c>invalid_request_error</c>
/// with 400/402/404) are business outcomes and return a <see cref="Result"/> failure. Everything
/// else — rate limits, authentication, idempotency conflicts, 5xx, network errors and timeouts —
/// propagates, so the worker abandons the message and the redelivery retries under the same key.
/// </para>
/// </summary>
internal sealed class StripePaymentProvider(
    IStripeClient stripeClient,
    ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    private const string RequiresCapture = "requires_capture";
    private const string Succeeded = "succeeded";
    private const string Canceled = "canceled";

    public string MethodName => "CreditCard";

    public async Task<Result<PaymentProviderResult>> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(stripeClient);
        PaymentIntent intent;
        try
        {
            intent = await service.CreateAsync(new PaymentIntentCreateOptions
            {
                Amount = ToMinorUnits(request.Amount),
                Currency = request.Currency.ToLowerInvariant(),
                CaptureMethod = "manual",
                Confirm = true,
                // Test-mode payment method that always succeeds; a live integration would
                // pass a PaymentMethod id collected client-side via Stripe Elements.
                PaymentMethod = "pm_card_visa",
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                },
                ReceiptEmail = request.CustomerEmail,
                // orderId lets the webhook find the record even before the worker has stored the
                // intent id; tenantId and correlationId tie the intent back to the saga.
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = request.OrderId.ToString(),
                    ["tenantId"] = request.TenantId.ToString(),
                    ["correlationId"] = request.CorrelationId.ToString()
                }
            }, Idempotent($"os-pi-create-{request.OrderId:N}"), ct);
        }
        catch (StripeException ex) when (IsPermanent(ex))
        {
            logger.LogWarning(ex, "Stripe authorization declined for order {OrderId}. Code: {StripeErrorCode}",
                request.OrderId, ex.StripeError?.Code);
            return Result<PaymentProviderResult>.Failure(PaymentErrors.AuthorizationFailed);
        }

        if (intent.Status != RequiresCapture)
        {
            // Confirmed with manual capture, anything but requires_capture (e.g. requires_action
            // for 3-D Secure) means no funds are held. Release the intent and report a decline.
            await VoidAsync(intent.Id, ct);
            logger.LogWarning(
                "Stripe PaymentIntent {IntentId} for order {OrderId} ended in status {Status} instead of requires_capture.",
                intent.Id, request.OrderId, intent.Status);
            return Result<PaymentProviderResult>.Failure(PaymentErrors.AuthorizationFailed);
        }

        logger.LogInformation("Stripe PaymentIntent {IntentId} authorized for order {OrderId}.",
            intent.Id, request.OrderId);
        return Result<PaymentProviderResult>.Success(new PaymentProviderResult(intent.Id));
    }

    public async Task<Result<PaymentProviderResult>> CaptureAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(stripeClient);
        try
        {
            var intent = await service.CaptureAsync(
                transactionId, new PaymentIntentCaptureOptions(), Idempotent($"os-pi-capture-{transactionId}"), ct);
            logger.LogInformation("Stripe PaymentIntent {IntentId} captured (status {Status}).", intent.Id, intent.Status);
            return Result<PaymentProviderResult>.Success(new PaymentProviderResult(intent.Id));
        }
        catch (StripeException ex) when (IsUnexpectedState(ex))
        {
            // The intent moved on without this call — typically captured by an earlier attempt
            // whose idempotency key has expired. Stripe's current state decides.
            var current = await service.GetAsync(transactionId, cancellationToken: ct);
            if (current.Status == Succeeded)
                return Result<PaymentProviderResult>.Success(new PaymentProviderResult(current.Id));

            logger.LogWarning("Stripe capture of intent {IntentId} rejected; intent is {Status}.", transactionId, current.Status);
            return Result<PaymentProviderResult>.Failure(PaymentErrors.CaptureFailed);
        }
        catch (StripeException ex) when (IsPermanent(ex))
        {
            logger.LogWarning(ex, "Stripe capture declined for intent {IntentId}. Code: {StripeErrorCode}",
                transactionId, ex.StripeError?.Code);
            return Result<PaymentProviderResult>.Failure(PaymentErrors.CaptureFailed);
        }
    }

    public async Task<Result> VoidAsync(string transactionId, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(stripeClient);
        try
        {
            await service.CancelAsync(
                transactionId, new PaymentIntentCancelOptions(), Idempotent($"os-pi-cancel-{transactionId}"), ct);
            logger.LogInformation("Stripe PaymentIntent {IntentId} canceled; authorization released.", transactionId);
            return Result.Success();
        }
        catch (StripeException ex) when (IsUnexpectedState(ex))
        {
            var current = await service.GetAsync(transactionId, cancellationToken: ct);
            if (current.Status == Canceled)
                return Result.Success();

            logger.LogWarning("Stripe cancel of intent {IntentId} rejected; intent is {Status}.", transactionId, current.Status);
            return Result.Failure(PaymentErrors.VoidFailed);
        }
        catch (StripeException ex) when (IsPermanent(ex))
        {
            logger.LogWarning(ex, "Stripe cancel rejected for intent {IntentId}. Code: {StripeErrorCode}",
                transactionId, ex.StripeError?.Code);
            return Result.Failure(PaymentErrors.VoidFailed);
        }
    }

    public async Task<Result> RefundAsync(string transactionId, decimal amount, string refundReference, CancellationToken ct = default)
    {
        var service = new RefundService(stripeClient);
        try
        {
            await service.CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = transactionId,
                Amount = ToMinorUnits(amount)
            }, Idempotent($"os-refund-{transactionId}-{refundReference}"), ct);
            logger.LogInformation("Stripe refund issued for intent {IntentId}, amount {Amount}.", transactionId, amount);
            return Result.Success();
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "charge_already_refunded")
        {
            // Refunded by an earlier attempt whose idempotency key has expired: the goal is met.
            logger.LogWarning("Stripe intent {IntentId} was already fully refunded.", transactionId);
            return Result.Success();
        }
        catch (StripeException ex) when (IsPermanent(ex))
        {
            logger.LogWarning(ex, "Stripe refund rejected for intent {IntentId}. Code: {StripeErrorCode}",
                transactionId, ex.StripeError?.Code);
            return Result.Failure(PaymentErrors.RefundFailed);
        }
    }

    private static RequestOptions Idempotent(string key) => new() { IdempotencyKey = key };

    // 429 (rate limit, lock timeout) also uses invalid_request_error; the status check keeps it
    // on the retry path together with 401/403 and 409.
    private static bool IsPermanent(StripeException ex) =>
        ex.StripeError?.Type is "card_error" or "invalid_request_error"
        && ex.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.PaymentRequired or HttpStatusCode.NotFound;

    private static bool IsUnexpectedState(StripeException ex) =>
        ex.StripeError?.Code == "payment_intent_unexpected_state";

    // Stripe expects amounts in the currency's minor unit (e.g. cents). Two-decimal
    // currencies (EUR/USD) are the showcase scope; rounding away from zero matches Money.
    private static long ToMinorUnits(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
