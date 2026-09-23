using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.Payment.Domain.Errors;

public static class PaymentErrors
{
    public static readonly Error PaymentNotFound = new("Payment.NotFound", "Payment record not found.", ErrorType.NotFound);
    public static readonly Error UnsupportedMethod = new("Payment.UnsupportedMethod", "Payment method is not supported.", ErrorType.Failure);
    public static readonly Error AuthorizationFailed = new("Payment.AuthorizationFailed", "Payment authorization failed.", ErrorType.Failure);
    public static readonly Error CaptureFailed = new("Payment.CaptureFailed", "Payment capture failed.", ErrorType.Failure);
    public static readonly Error RefundFailed = new("Payment.RefundFailed", "Payment refund failed.", ErrorType.Failure);
    public static readonly Error VoidFailed = new("Payment.VoidFailed", "Releasing the payment authorization failed.", ErrorType.Failure);
    public static readonly Error InvalidStatusTransition = new("Payment.InvalidStatusTransition", "The payment's current status does not allow this transition.", ErrorType.Conflict);
    public static readonly Error DuplicatePayment = new("Payment.Duplicate", "A payment for this order already exists.", ErrorType.Conflict);
}
