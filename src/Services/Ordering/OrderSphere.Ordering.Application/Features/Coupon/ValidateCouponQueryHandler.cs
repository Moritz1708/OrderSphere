using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Application.Models;
using OrderSphere.Ordering.Domain.Errors;

namespace OrderSphere.Ordering.Application.Features.Coupon;

public sealed class ValidateCouponQueryHandler(
    IOrderingDbContext context,
    ILogger<ValidateCouponQueryHandler> logger)
    : IQueryHandler<ValidateCouponQuery, Result<CouponValidationDto>>
{
    public async Task<Result<CouponValidationDto>> Handle(ValidateCouponQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Result<CouponValidationDto>.Failure(CouponErrors.CodeRequired);

        var normalized = Domain.Entities.Coupon.Normalize(request.Code);
        var coupon = await context.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == normalized, cancellationToken);

        if (coupon is null)
        {
            // Warning, matching AddToCartCommandHandler: both are lookup misses driven by
            // client input that are returned as a Result failure (see docs/logging.md).
            logger.LogWarning("Coupon code not found: {Code}", request.Code);
            return Result<CouponValidationDto>.Failure(CouponErrors.InvalidCode);
        }

        var discountResult = coupon.CalculateDiscount(request.Subtotal, DateTime.UtcNow);
        if (discountResult.IsFailure)
        {
            // Not-yet-valid / min-subtotal / usage-limit surface as a non-applicable coupon rather
            // than a hard error, so the checkout UI can show the reason without blocking the page.
            return Result<CouponValidationDto>.Success(new CouponValidationDto(
                coupon.Code,
                IsValid: false,
                DiscountAmount: 0m,
                Message: discountResult.Error.Description));
        }

        return Result<CouponValidationDto>.Success(new CouponValidationDto(
            coupon.Code,
            IsValid: true,
            DiscountAmount: discountResult.Value,
            Message: "Gutschein wurde angewendet."));
    }
}
