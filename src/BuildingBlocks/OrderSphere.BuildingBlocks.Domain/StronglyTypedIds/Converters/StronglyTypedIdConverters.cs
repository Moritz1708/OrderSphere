using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

// Converters reside in the same namespace as the ID types so a single
// `using OrderSphere.BuildingBlocks.StronglyTypedIds;` makes both available.
namespace OrderSphere.BuildingBlocks.StronglyTypedIds;

// One ValueConverter<TId, Guid> per typed ID.
// Used in each DbContext's ConfigureConventions:
//   configurationBuilder.Properties<ProductId>().HaveConversion<ProductIdConverter>();

public sealed class CustomerIdConverter : ValueConverter<CustomerId, Guid>
{
    public CustomerIdConverter() : base(id => id.Value, v => new CustomerId(v)) { }
}

public sealed class ProductIdConverter : ValueConverter<ProductId, Guid>
{
    public ProductIdConverter() : base(id => id.Value, v => new ProductId(v)) { }
}

public sealed class OrderIdConverter : ValueConverter<OrderId, Guid>
{
    public OrderIdConverter() : base(id => id.Value, v => new OrderId(v)) { }
}

public sealed class CategoryIdConverter : ValueConverter<CategoryId, Guid>
{
    public CategoryIdConverter() : base(id => id.Value, v => new CategoryId(v)) { }
}

public sealed class BrandIdConverter : ValueConverter<BrandId, Guid>
{
    public BrandIdConverter() : base(id => id.Value, v => new BrandId(v)) { }
}

public sealed class CartIdConverter : ValueConverter<CartId, Guid>
{
    public CartIdConverter() : base(id => id.Value, v => new CartId(v)) { }
}

public sealed class CartItemIdConverter : ValueConverter<CartItemId, Guid>
{
    public CartItemIdConverter() : base(id => id.Value, v => new CartItemId(v)) { }
}

public sealed class PaymentIdConverter : ValueConverter<PaymentId, Guid>
{
    public PaymentIdConverter() : base(id => id.Value, v => new PaymentId(v)) { }
}

public sealed class WebhookSubscriptionIdConverter : ValueConverter<WebhookSubscriptionId, Guid>
{
    public WebhookSubscriptionIdConverter() : base(id => id.Value, v => new WebhookSubscriptionId(v)) { }
}

public sealed class WebhookDeliveryIdConverter : ValueConverter<WebhookDeliveryId, Guid>
{
    public WebhookDeliveryIdConverter() : base(id => id.Value, v => new WebhookDeliveryId(v)) { }
}

public sealed class CustomerProfileIdConverter : ValueConverter<CustomerProfileId, Guid>
{
    public CustomerProfileIdConverter() : base(id => id.Value, v => new CustomerProfileId(v)) { }
}

public sealed class SavedAddressIdConverter : ValueConverter<SavedAddressId, Guid>
{
    public SavedAddressIdConverter() : base(id => id.Value, v => new SavedAddressId(v)) { }
}

public sealed class OrderItemIdConverter : ValueConverter<OrderItemId, Guid>
{
    public OrderItemIdConverter() : base(id => id.Value, v => new OrderItemId(v)) { }
}

public sealed class CouponIdConverter : ValueConverter<CouponId, Guid>
{
    public CouponIdConverter() : base(id => id.Value, v => new CouponId(v)) { }
}

public sealed class ReturnRequestIdConverter : ValueConverter<ReturnRequestId, Guid>
{
    public ReturnRequestIdConverter() : base(id => id.Value, v => new ReturnRequestId(v)) { }
}

public sealed class ReviewIdConverter : ValueConverter<ReviewId, Guid>
{
    public ReviewIdConverter() : base(id => id.Value, v => new ReviewId(v)) { }
}

public sealed class ReservationIdConverter : ValueConverter<ReservationId, Guid>
{
    public ReservationIdConverter() : base(id => id.Value, v => new ReservationId(v)) { }
}

public sealed class InvoiceIdConverter : ValueConverter<InvoiceId, Guid>
{
    public InvoiceIdConverter() : base(id => id.Value, v => new InvoiceId(v)) { }
}

public sealed class InvoiceAdjustmentIdConverter : ValueConverter<InvoiceAdjustmentId, Guid>
{
    public InvoiceAdjustmentIdConverter() : base(id => id.Value, v => new InvoiceAdjustmentId(v)) { }
}

public sealed class TenantAggregateIdConverter : ValueConverter<TenantAggregateId, Guid>
{
    public TenantAggregateIdConverter() : base(id => id.Value, v => new TenantAggregateId(v)) { }
}
