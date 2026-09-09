using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Outbox;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.BuildingBlocks.Extensions;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Application.Abstractions;
using OrderSphere.Payment.Domain.Entities;

namespace OrderSphere.Payment.Infrastructure.Persistence;

public sealed class PaymentDbContext(
    DbContextOptions<PaymentDbContext> options,
    IPublisher publisher,
    ICurrentUser currentUser,
    ITenantContext tenantContext) : DbContext(options), IPaymentDbContext
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    internal DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public void AddOutboxMessage(string type, string content)
        => OutboxMessages.Add(OutboxMessage.Create(type, content));

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
        configurationBuilder.Properties<PaymentId>().HaveConversion<PaymentIdConverter>();
        configurationBuilder.Properties<OrderId>().HaveConversion<OrderIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyTenantQueryFilter(() => tenantContext.TenantId);

        // xmin is a PostgreSQL system column — only configure it when the provider is Npgsql.
        // SQLite (used in tests) and other providers do not support the xid column type.
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            modelBuilder.Entity<OutboxMessage>()
                .Property<uint>("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        }

        base.OnModelCreating(modelBuilder);
    }
}
