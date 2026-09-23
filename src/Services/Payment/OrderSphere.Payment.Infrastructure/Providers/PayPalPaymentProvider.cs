using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.Payment.Infrastructure.Providers;

internal sealed class PayPalPaymentProvider(ILogger<PayPalPaymentProvider> logger) : IPaymentProvider
{
    public string MethodName => "PayPal";

    public Task<Result<PaymentProviderResult>> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        // Simulated provider — replace with PayPal SDK integration.
        var transactionId = $"PP-{Guid.CreateVersion7():N}";
        logger.LogInformation("PayPal payment authorized for order {OrderId}. TransactionId: {TransactionId}",
            request.OrderId, transactionId);

        return Task.FromResult(Result<PaymentProviderResult>.Success(new PaymentProviderResult(transactionId)));
    }

    public Task<Result<PaymentProviderResult>> CaptureAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        logger.LogInformation("PayPal payment captured. TransactionId: {TransactionId}, Amount: {Amount}",
            transactionId, amount);

        return Task.FromResult(Result<PaymentProviderResult>.Success(new PaymentProviderResult(transactionId)));
    }

    public Task<Result> VoidAsync(string transactionId, CancellationToken ct = default)
    {
        logger.LogInformation("PayPal authorization voided. TransactionId: {TransactionId}", transactionId);

        return Task.FromResult(Result.Success());
    }

    public Task<Result> RefundAsync(string transactionId, decimal amount, string refundReference, CancellationToken ct = default)
    {
        logger.LogInformation("PayPal payment refunded. TransactionId: {TransactionId}, Amount: {Amount}",
            transactionId, amount);

        return Task.FromResult(Result.Success());
    }
}
