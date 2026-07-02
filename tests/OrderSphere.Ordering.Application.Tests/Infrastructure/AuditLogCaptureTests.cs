using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Ordering.Application.Tests.Helpers;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Application.Tests.Infrastructure;

/// <summary>
/// Verifies D2's cross-cutting audit trail: <c>OrderingDbContext.SaveChangesAsync</c> calls
/// <c>ChangeTracker.CaptureAuditLog</c>, which stages one <see cref="AuditLogEntry"/> per
/// added/modified <see cref="OrderSphere.BuildingBlocks.Abstraction.IAuditableEntity"/>.
/// </summary>
public sealed class AuditLogCaptureTests
{
    private static Coupon NewCoupon() =>
        new("SAVE10", DiscountType.Percentage, 10m, minSubtotal: null,
            validFrom: null, validUntil: null, maxRedemptions: null);

    [Fact]
    public async Task SaveChanges_OnAdd_WritesCreatedAuditLogEntry()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Sub.Returns("auth0|admin-1");

        await using var context = OrderingDbContextFactory.Create(currentUser);
        var coupon = NewCoupon();
        context.Coupons.Add(coupon);

        await context.SaveChangesAsync();

        var entries = await context.Set<AuditLogEntry>().ToListAsync();
        entries.Should().ContainSingle();
        entries[0].EntityType.Should().Be(nameof(Coupon));
        entries[0].EntityId.Should().Be(coupon.Id.Value.ToString());
        entries[0].Action.Should().Be(AuditAction.Created);
        entries[0].ChangedBy.Should().Be("auth0|admin-1");
    }

    [Fact]
    public async Task SaveChanges_OnModify_WritesModifiedAuditLogEntryWithDiff()
    {
        await using var context = OrderingDbContextFactory.Create();
        var coupon = NewCoupon();
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        coupon.Deactivate();
        await context.SaveChangesAsync();

        var entries = await context.Set<AuditLogEntry>()
            .Where(e => e.Action == AuditAction.Modified)
            .ToListAsync();

        entries.Should().ContainSingle();
        entries[0].Changes.Should().Contain(nameof(Coupon.IsActive));
    }

    [Fact]
    public async Task SaveChanges_NoActualPropertyChange_WritesNoModifiedEntry()
    {
        await using var context = OrderingDbContextFactory.Create();
        var coupon = NewCoupon();
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var reloaded = await context.Coupons.SingleAsync(c => c.Id == coupon.Id);
        var entry = context.Entry(reloaded);
        entry.State = EntityState.Modified;
        foreach (var property in entry.Properties)
            property.IsModified = false;

        await context.SaveChangesAsync();

        var modifiedEntries = await context.Set<AuditLogEntry>()
            .Where(e => e.Action == AuditAction.Modified)
            .ToListAsync();
        modifiedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task EfAuditLogQuery_FiltersByEntityTypeAndId()
    {
        await using var context = OrderingDbContextFactory.Create();
        var coupon = NewCoupon();
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        var query = new EfAuditLogQuery<OrderingDbContext>(context);
        var result = await query.QueryAsync(nameof(Coupon), coupon.Id.Value.ToString(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => e.Action == nameof(AuditAction.Created));
    }
}
