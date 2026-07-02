using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.Enums;

namespace OrderSphere.Ordering.Infrastructure.EntityConfigurations;

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    private static readonly List<Guid> EmptyCategoryIds = [];

    // Compare the category-scope collection structurally. Without this, EF's model differ
    // compares the seeded empty list by reference and reports a perpetual pending change
    // (an empty-list -> empty-list UpdateData) on every model build.
    private static readonly ValueComparer<List<Guid>> CategoryIdsComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        value => value.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
        value => value.ToList());

    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons");
        builder.HasKey(c => c.Id);
        builder.HasQueryFilter(c => !c.IsDeleted);

        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.DiscountType).HasConversion<int>().IsRequired();
        builder.Property(c => c.Value).HasPrecision(18, 2).IsRequired();
        builder.Property(c => c.MinSubtotal).HasPrecision(18, 2);
        builder.Property(c => c.RedeemedCount).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();

        // Tiered discount thresholds stored as a JSON column; empty for Flat/Percentage coupons.
        builder.OwnsMany(c => c.Tiers, tiers =>
        {
            tiers.ToJson("tiers");
            tiers.Property(t => t.MinSubtotal).HasPrecision(18, 2);
            tiers.Property(t => t.DiscountValue).HasPrecision(18, 2);
        });

        // Optional category scope stored as a primitive JSON collection; empty = all categories.
        var scopedCategoryIds = builder.PrimitiveCollection(c => c.ScopedCategoryIds);
        scopedCategoryIds.HasColumnName("scoped_category_ids");
        scopedCategoryIds.Metadata.SetValueComparer(CategoryIdsComparer);

        // Seed the two codes the former hardcoded handler supported, so behavior is preserved.
        var seedCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new
            {
                Id = CouponId.From(new Guid("0192a000-0000-7000-8000-000000000001")),
                Code = "WELCOME10",
                DiscountType = DiscountType.Flat,
                Value = 10m,
                MinSubtotal = (decimal?)null,
                ValidFrom = (DateTime?)null,
                ValidUntil = (DateTime?)null,
                MaxRedemptions = (int?)null,
                RedeemedCount = 0,
                IsActive = true,
                ScopedCategoryIds = EmptyCategoryIds,
                CreatedAt = seedCreatedAt,
                IsDeleted = false,
                TenantId = TenantId.Default,
            },
            new
            {
                Id = CouponId.From(new Guid("0192a000-0000-7000-8000-000000000002")),
                Code = "SUMMER15",
                DiscountType = DiscountType.Flat,
                Value = 15m,
                MinSubtotal = (decimal?)100m,
                ValidFrom = (DateTime?)null,
                ValidUntil = (DateTime?)null,
                MaxRedemptions = (int?)null,
                RedeemedCount = 0,
                IsActive = true,
                ScopedCategoryIds = EmptyCategoryIds,
                CreatedAt = seedCreatedAt,
                IsDeleted = false,
                TenantId = TenantId.Default,
            });
    }
}
