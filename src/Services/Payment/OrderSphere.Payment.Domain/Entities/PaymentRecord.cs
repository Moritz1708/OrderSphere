using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.BuildingBlocks.ValueObjects;
using OrderSphere.Payment.Domain.DomainEvents;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Domain.Errors;

namespace OrderSphere.Payment.Domain.Entities;

public class PaymentRecord : AuditableEntity<PaymentId>, IAggregateRoot
{
    public OrderId OrderId { get; private set; }

    /// <summary>The charged amount and its currency as a single value object.</summary>
    public Money Amount { get; private set; } = null!;
    public string PaymentMethod { get; private set; } = "";
    public string CustomerEmail { get; private set; } = "";
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string? TransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public Guid CorrelationId { get; private set; }

    private PaymentRecord()
    {
        OrderId = OrderId.Empty;
    }

    public PaymentRecord(
        OrderId orderId,
        decimal amount,
        string currency,
        string paymentMethod,
        string customerEmail,
        Guid correlationId)
    {
        Id = PaymentId.New();
        OrderId = orderId;
        Amount = Money.Of(amount, currency);
        PaymentMethod = paymentMethod;
        CustomerEmail = customerEmail;
        CorrelationId = correlationId;
        Status = PaymentStatus.Pending;
    }

    // Valid transitions: Pending → Authorized → Captured → Refunded, and Pending|Authorized → Failed.
    // Repeating the current state (same transaction id) succeeds without a new domain event, so
    // redelivered messages and webhooks are idempotent. Capture may replace the authorization id:
    // some providers issue a separate capture reference.

    public Result MarkAuthorized(string transactionId)
    {
        if (Status == PaymentStatus.Authorized && TransactionId == transactionId)
            return Result.Success();
        if (Status != PaymentStatus.Pending)
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        TransactionId = transactionId;
        Status = PaymentStatus.Authorized;
        RaiseDomainEvent(new PaymentAuthorizedDomainEvent(Id, OrderId, transactionId));
        return Result.Success();
    }

    public Result MarkCaptured(string transactionId)
    {
        if (Status == PaymentStatus.Captured)
            return TransactionId == transactionId
                ? Result.Success()
                : Result.Failure(PaymentErrors.InvalidStatusTransition);
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Authorized))
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        TransactionId = transactionId;
        Status = PaymentStatus.Captured;
        RaiseDomainEvent(new PaymentCapturedDomainEvent(Id, OrderId, transactionId));
        return Result.Success();
    }

    public Result MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Failed)
            return Result.Success();
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Authorized))
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        FailureReason = reason;
        Status = PaymentStatus.Failed;
        RaiseDomainEvent(new PaymentFailedDomainEvent(Id, OrderId, reason));
        return Result.Success();
    }

    public Result MarkRefunded()
    {
        if (Status == PaymentStatus.Refunded)
            return Result.Success();
        if (Status != PaymentStatus.Captured)
            return Result.Failure(PaymentErrors.InvalidStatusTransition);

        Status = PaymentStatus.Refunded;
        return Result.Success();
    }

    /// <summary>GDPR right-to-erasure: overwrites the customer email, keeping the payment
    /// record (amount, status, transaction id) for financial retention.</summary>
    public void AnonymizeCustomerEmail()
    {
        CustomerEmail = $"erased-{Id.Value}@erased.invalid";
    }
}
