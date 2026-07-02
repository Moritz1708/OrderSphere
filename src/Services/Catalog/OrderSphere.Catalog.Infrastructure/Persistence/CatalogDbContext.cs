using MediatR;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.Extensions;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;

namespace OrderSphere.Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    IPublisher publisher,
    ICurrentUser currentUser,
    ITenantContext tenantContext) : DbContext(options), ICatalogDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<ProductReview> Reviews => Set<ProductReview>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.ApplyAuditFields(tenantContext.TenantId);
        ChangeTracker.CaptureAuditLog(currentUser);

        var events = ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .SelectMany(e => e.PopDomainEvents())
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var @event in events)
            await publisher.Publish(@event, cancellationToken);

        return result;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ProductId>().HaveConversion<ProductIdConverter>();
        configurationBuilder.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        configurationBuilder.Properties<BrandId>().HaveConversion<BrandIdConverter>();
        configurationBuilder.Properties<ReviewId>().HaveConversion<ReviewIdConverter>();
        configurationBuilder.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        configurationBuilder.Properties<ReservationId>().HaveConversion<ReservationIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyTenantQueryFilter(() => tenantContext.TenantId);
    }
}
