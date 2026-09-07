using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Webhooks.Tests.Helpers;
using TenantIdHelper = OrderSphere.BuildingBlocks.StronglyTypedIds.TenantId;

namespace OrderSphere.Webhooks.Tests.Persistence;

/// <summary>
/// Executable specification of the migration hazard in ADR 0012.
/// <para>
/// Auth0 Organizations is not enabled in any environment today, so every row in every
/// tenant-filtered context carries <c>TenantId.Default</c> (<see cref="Guid.Empty"/>). The tenant
/// query filter (<c>ModelBuilderExtensions.ApplyTenantQueryFilter</c>) is strict equality with no
/// <c>Guid.Empty</c> exemption. The moment the first real <c>org_id</c> claim is issued, every
/// pre-existing row for that organisation stops matching.
/// </para>
/// <para>
/// These tests pass today — they assert the hazard, not a defect. Webhooks is the worst instance
/// of it because the loss is silent: <c>WebhookEventProcessor</c> reports a subscription lookup
/// that matches nothing as <c>"Created 0 webhook deliveries"</c> at Information, so no alert
/// fires and no message dead-letters. If a backfill is ever added, the first test is the one that
/// must be revisited.
/// </para>
/// </summary>
public sealed class TenantBackfillHazardTests
{
    private const string OrgId = "org_backfill_hazard";
    private static readonly Guid RealTenant = TenantIdHelper.FromOrgId(OrgId);

    /// <summary>
    /// Stands in for the switch-over: the same database, read first by a process with no
    /// organisation claim and then by one that has one.
    /// </summary>
    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; } = TenantIdHelper.Default;
    }

    private static WebhookSubscription CreateSubscription() =>
        new(CustomerId.New(), "https://example.com/hook", "secret", [WebhookEventType.OrderPlaced]);

    [Fact]
    public async Task Rows_written_before_organizations_are_enabled_are_invisible_to_a_real_tenant()
    {
        var tenant = new MutableTenantContext();
        await using var ctx = WebhooksDbContextFactory.Create(tenant);

        // Written today: no org_id claim in circulation, so the row is stamped Guid.Empty.
        ctx.Subscriptions.Add(CreateSubscription());
        await ctx.SaveChangesAsync();

        // Auth0 Organizations is switched on; the same worker now resolves a real tenant.
        tenant.TenantId = RealTenant;

        var visible = await ctx.Subscriptions.ToListAsync();
        visible.Should().BeEmpty("the strict-equality tenant filter excludes Guid.Empty rows");

        // The row was not deleted — it is filtered out. That is what makes the loss silent.
        var stored = await ctx.Subscriptions.IgnoreQueryFilters().ToListAsync();
        stored.Should().ContainSingle().Which.TenantId.Should().Be(TenantIdHelper.Default);
    }

    [Fact]
    public async Task Rows_backfilled_to_the_real_tenant_become_visible_again()
    {
        var tenant = new MutableTenantContext();
        await using var ctx = WebhooksDbContextFactory.Create(tenant);

        ctx.Subscriptions.Add(CreateSubscription());
        await ctx.SaveChangesAsync();

        tenant.TenantId = RealTenant;

        // What the backfill migration does per table, before the deploy that issues the claims.
        var row = await ctx.Subscriptions.IgnoreQueryFilters().SingleAsync();
        row.TenantId = RealTenant;
        await ctx.SaveChangesAsync();

        var visible = await ctx.Subscriptions.ToListAsync();
        visible.Should().ContainSingle();
    }
}
