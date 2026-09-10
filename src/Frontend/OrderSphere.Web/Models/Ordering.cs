namespace OrderSphere.Web.Models;

// Cart
public sealed record CartDto(Guid CustomerId, List<CartItemDto> Items);
public sealed record CartItemDto(Guid ProductId, string ProductName, decimal Price, int Quantity);

// Orders
public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    string PaymentMethod,
    string? TrackingNumber,
    OrderShippingAddressDto ShippingAddress,
    List<OrderLineDto> Items,
    decimal Total,
    decimal DiscountAmount,
    string? CouponCode,
    DateTime CreatedAt,
    decimal ShippingCost = 0,
    List<OrderStatusEntryDto>? StatusHistory = null);

public sealed record OrderStatusEntryDto(
    string Status,
    DateTime OccurredAt,
    string? Note);

public sealed record OrderShippingAddressDto(
    string FirstName,
    string LastName,
    string Street,
    string City,
    string PostalCode,
    string Country);

public sealed record OrderLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal Price);

// Checkout
// CustomerId, CustomerEmail, and CustomerName are derived from the JWT token server-side.
public sealed record CheckoutRequest(
    CheckoutAddress ShippingAddress,
    int PaymentMethod,
    string? CouponCode = null);

public sealed record CheckoutAddress(
    string FirstName,
    string LastName,
    string Street,
    string City,
    string PostalCode,
    string Country);

// Coupon
public sealed record CouponValidationDto(bool IsValid, decimal DiscountAmount, string? Message);

// Invoice — metadata returned by the Invoicing service; the PDF itself is streamed separately.
public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    Guid OrderId,
    string CustomerEmail,
    string CustomerName,
    decimal Total,
    DateTime IssuedAt);

// Editable form-state for the checkout page (bound across the address/payment sub-forms).
public sealed class CheckoutFormModel
{
    public AddressFormModel Address { get; } = new();
    public int PaymentMethod { get; set; }
}
