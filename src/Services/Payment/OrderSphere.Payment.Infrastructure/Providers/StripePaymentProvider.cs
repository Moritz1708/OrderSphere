using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.Payment.Domain.Errors;
using Stripe;

namespace OrderSphere.Payment.Infrastructure.Providers;

/// <summary>
/// Real payment provider backed by Stripe (test mode). Maps the three-stage
/// Authorize → Capture → Refund flow onto Stripe PaymentIntents with manual capture.
/// Registered under the "CreditCard" method name so existing checkout routes here without a
/// UI contract change. Stripe SDK exceptions are mapped to <see cref="Result"/> failures —
/// business outcomes never surface as exceptions.
/// </summary>
internal sealed class StripePaymentProvider(
    IStripeClient stripeClient,
    ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    public string MethodName => "CreditCard";

    public async Task<Result<PaymentProviderResult>> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(stripeClient);
        try
        {
            var intent = await service.CreateAsync(new PaymentIntentCreateOptions
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
                Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() }
            }, cancellationToken: ct);

            logger.LogInformation(
                "Stripe PaymentIntent {IntentId} authorized for order {OrderId} (status {Status}).",
                intent.Id, request.OrderId, intent.Status);

            return Result<PaymentProviderResult>.Success(new PaymentProviderResult(intent.Id));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe authorization failed for order {OrderId}.", request.OrderId);
            return Result<PaymentProviderResult>.Failure(PaymentErrors.AuthorizationFailed);
        }
    }

    public async Task<Result<PaymentProviderResult>> CaptureAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(stripeClient);
        try
        {
            var intent = await service.CaptureAsync(transactionId, new PaymentIntentCaptureOptions(), cancellationToken: ct);
            logger.LogInformation("Stripe PaymentIntent {IntentId} capture requested (status {Status}).",
                intent.Id, intent.Status);
            return Result<PaymentProviderResult>.Success(new PaymentProviderResult(intent.Id));
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe capture failed for intent {IntentId}.", transactionId);
            return Result<PaymentProviderResult>.Failure(PaymentErrors.CaptureFailed);
        }
    }

    public async Task<Result> RefundAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        var service = new RefundService(stripeClient);
        try
        {
            await service.CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = transactionId,
                Amount = ToMinorUnits(amount)
            }, cancellationToken: ct);
            logger.LogInformation("Stripe refund issued for intent {IntentId}, amount {Amount}.", transactionId, amount);
            return Result.Success();
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe refund failed for intent {IntentId}.", transactionId);
            return Result.Failure(PaymentErrors.RefundFailed);
        }
    }

    // Stripe expects amounts in the currency's minor unit (e.g. cents). Two-decimal
    // currencies (EUR/USD) are the showcase scope; rounding away from zero matches Money.
    private static long ToMinorUnits(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
