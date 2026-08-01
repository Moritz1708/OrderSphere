using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.Extensions;

namespace OrderSphere.Invoicing.Infrastructure.Persistence;

public sealed class InvoicingDbContext(
    DbContextOptions<InvoicingDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : DbContext(options), IInvoicingDbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceAdjustment> InvoiceAdjustments => Set<InvoiceAdjustment>();

    // Infrastructure-only counter backing the gapless invoice number sequence; not on the interface.
    internal DbSet<InvoiceNumberCounter> InvoiceNumberCounters => Set<InvoiceNumberCounter>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    // The Npgsql resiliency strategy (wired via EnrichNpgsqlDbContext) forbids user-managed
    // transactions spanning retries, so the whole unit of work — including non-DB side effects
    // like PDF rendering and blob upload — is retried together per Microsoft's execution-strategy
    // guidance. Callers must keep the delegate free of external effects that aren't safe to repeat.
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);
            try
            {
                var result = await operation(ct);
                await SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.ApplyAuditFields(tenantContext.TenantId);
        ChangeTracker.CaptureAuditLog(currentUser);
        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<InvoiceId>().HaveConversion<InvoiceIdConverter>();
        configurationBuilder.Properties<InvoiceAdjustmentId>().HaveConversion<InvoiceAdjustmentIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvoicingDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyTenantQueryFilter(() => tenantContext.TenantId);
    }
}
