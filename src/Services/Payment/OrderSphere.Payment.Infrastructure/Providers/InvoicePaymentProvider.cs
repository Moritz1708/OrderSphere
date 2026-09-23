using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.Payment.Infrastructure.Providers;

internal sealed class InvoicePaymentProvider(ILogger<InvoicePaymentProvider> logger) : IPaymentProvider
{
    public string MethodName => "Invoice";

    public Task<Result<PaymentProviderResult>> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var transactionId = $"INV-{Guid.CreateVersion7():N}";
        logger.LogInformation("Invoice payment authorized for order {OrderId}. TransactionId: {TransactionId}",
            request.OrderId, transactionId);

        return Task.FromResult(Result<PaymentProviderResult>.Success(new PaymentProviderResult(transactionId)));
    }

    public Task<Result<PaymentProviderResult>> CaptureAsync(string transactionId, decimal amount, CancellationToken ct = default)
    {
        logger.LogInformation("Invoice payment captured. TransactionId: {TransactionId}, Amount: {Amount}",
            transactionId, amount);

        return Task.FromResult(Result<PaymentProviderResult>.Success(new PaymentProviderResult(transactionId)));
    }

    public Task<Result> VoidAsync(string transactionId, CancellationToken ct = default)
    {
        logger.LogInformation("Invoice authorization voided. TransactionId: {TransactionId}", transactionId);

        return Task.FromResult(Result.Success());
    }

    public Task<Result> RefundAsync(string transactionId, decimal amount, string refundReference, CancellationToken ct = default)
    {
        logger.LogInformation("Invoice payment refunded. TransactionId: {TransactionId}, Amount: {Amount}",
            transactionId, amount);

        return Task.FromResult(Result.Success());
    }
}
