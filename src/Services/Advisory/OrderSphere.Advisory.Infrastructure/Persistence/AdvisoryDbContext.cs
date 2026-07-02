using Microsoft.EntityFrameworkCore;
using OrderSphere.Advisory.Application.Abstractions;
using OrderSphere.Advisory.Domain.Entities;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.Extensions;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Advisory.Infrastructure.Persistence;

public sealed class AdvisoryDbContext(
    DbContextOptions<AdvisoryDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : DbContext(options), IAdvisoryDbContext
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.ApplyAuditFields(tenantContext.TenantId);
        ChangeTracker.CaptureAuditLog(currentUser);
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdvisoryDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyTenantQueryFilter(() => tenantContext.TenantId);
        base.OnModelCreating(modelBuilder);
    }
}
