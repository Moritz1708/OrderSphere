using OrderSphere.Payment.Domain.Enums;

namespace OrderSphere.Payment.Api.Logging;

/// <summary>
/// Source-generated log methods for the Payment API. EventIds 5001-5099 (Payment range
/// 5000-5999, see docs/logging.md).
/// </summary>
internal static partial class PaymentApiLog
{
    /// <summary>
    /// Stripe and the local record disagree in a way no automatic transition resolves — for
    /// example Stripe reports a capture for a payment recorded as failed, so the customer may have
    /// been charged for a cancelled order. An operator must reconcile it in the Stripe dashboard.
    /// </summary>
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Stripe reconciliation required: {EventType} for intent {IntentId} contradicts the payment for order {OrderId} (status {Status}, transaction {TransactionId}).")]
    public static partial void StripeReconciliationRequired(
        this ILogger logger,
        string eventType,
        string intentId,
        Guid orderId,
        PaymentStatus status,
        string? transactionId);
}
