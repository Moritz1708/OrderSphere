using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Partners.Tests.Helpers;

namespace OrderSphere.Partners.Tests.Persistence;

/// <summary>
/// Verifies the org-level tenant query filter (ADR 0012) also applies to the Partners service's
/// own data — a tenant must never see a partner account created under a different tenant.
/// </summary>
public sealed class PartnerTenantIsolationTests
{
    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    [Fact]
    public async Task TenantA_CannotSeePartners_CreatedByTenantB()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        await using (var seedContext = PartnersDbContextFactory.Create(connection, tenantA))
        {
            seedContext.Database.EnsureCreated();
            seedContext.Partners.Add(Partner.Create("Tenant A Partner", QuotaTier.Standard).Partner);
            await seedContext.SaveChangesAsync();
        }

        await using (var seedContext = PartnersDbContextFactory.Create(connection, tenantB))
        {
            seedContext.Partners.Add(Partner.Create("Tenant B Partner", QuotaTier.Standard).Partner);
            await seedContext.SaveChangesAsync();
        }

        await using var asTenantA = PartnersDbContextFactory.Create(connection, tenantA);
        await using var asTenantB = PartnersDbContextFactory.Create(connection, tenantB);

        (await asTenantA.Partners.ToListAsync()).Should().ContainSingle()
            .Which.Name.Should().Be("Tenant A Partner");

        (await asTenantB.Partners.ToListAsync()).Should().ContainSingle()
            .Which.Name.Should().Be("Tenant B Partner");
    }
}
