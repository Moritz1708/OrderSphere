using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Extensions;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Partners.Application.Abstractions;
using OrderSphere.Partners.Domain.Entities;

namespace OrderSphere.Partners.Infrastructure.Persistence;

public sealed class PartnersDbContext(
    DbContextOptions<PartnersDbContext> options,
    ITenantContext tenantContext) : DbContext(options), IPartnersDbContext
{
    public DbSet<Partner> Partners => Set<Partner>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.ApplyAuditFields(tenantContext.TenantId);
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<PartnerId>().HaveConversion<PartnerIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PartnersDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilter(() => tenantContext.TenantId);
        base.OnModelCreating(modelBuilder);
    }
}
