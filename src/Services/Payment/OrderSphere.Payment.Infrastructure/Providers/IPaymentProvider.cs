using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.Payment.Infrastructure.Providers;

/// <summary>
/// A payment service provider. Business outcomes (declined card, rejected request) are returned
/// as <see cref="Result"/> failures. Transient faults — network errors, timeouts, rate limits,
/// provider outages — are thrown, so the calling worker abandons the message and Service Bus
/// redelivers it; providers must make every call idempotent so that the redelivery completes or
/// replays the original request instead of repeating it.
/// </summary>
public interface IPaymentProvider
{
    string MethodName { get; }
    Task<Result<PaymentProviderResult>> AuthorizeAsync(PaymentRequest request, CancellationToken ct = default);
    Task<Result<PaymentProviderResult>> CaptureAsync(string transactionId, decimal amount, CancellationToken ct = default);

    /// <summary>Releases an authorization that will not be captured.</summary>
    Task<Result> VoidAsync(string transactionId, CancellationToken ct = default);

    /// <summary>
    /// Refunds <paramref name="amount"/> of a captured payment. <paramref name="refundReference"/>
    /// identifies the business reason (a return request, a failed order confirmation) and makes the
    /// refund idempotent per reason, so partial refunds for different returns stay distinct.
    /// </summary>
    Task<Result> RefundAsync(string transactionId, decimal amount, string refundReference, CancellationToken ct = default);
}

public sealed record PaymentRequest(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    Guid TenantId,
    Guid CorrelationId);

public sealed record PaymentProviderResult(string TransactionId);
