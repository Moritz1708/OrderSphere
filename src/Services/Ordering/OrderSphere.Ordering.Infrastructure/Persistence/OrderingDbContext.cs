using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Outbox;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.BuildingBlocks.Extensions;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.BuildingBlocks.ValueObjects;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.ReadModels;
using OrderSphere.Ordering.Infrastructure.EventSourcing;

namespace OrderSphere.Ordering.Infrastructure.Persistence;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    IPublisher publisher,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : DbContext(options), IOrderingDbContext
{
    public DbSet<OrderView> Orders => Set<OrderView>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    internal DbSet<OrderEventRecord> OrderEvents => Set<OrderEventRecord>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<OrderSaga> OrderSagas => Set<OrderSaga>();
    public DbSet<OrderHistoryEntry> OrderHistory => Set<OrderHistoryEntry>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    internal DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public void AddOutboxMessage(string type, string content)
        => OutboxMessages.Add(new OutboxMessage
        {
            Type = type,
            Content = content,
            // Capture the current trace context so the asynchronous dispatch joins this trace.
            TraceParent = Activity.Current?.Id
        });
    internal DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
            throw new InvalidOperationException("A transaction is already active.");

        _transaction = await Database.BeginTransactionAsync(ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction.");

        try
        {
            await SaveChangesAsync(ct);
            await _transaction.CommitAsync(ct);
        }
        catch
        {
            await _transaction.DisposeAsync();
            _transaction = null;
            throw;
        }

        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            return;

        try
        {
            await _transaction.RollbackAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

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
        configurationBuilder.Properties<OrderId>().HaveConversion<OrderIdConverter>();
        configurationBuilder.Properties<OrderItemId>().HaveConversion<OrderItemIdConverter>();
        configurationBuilder.Properties<CouponId>().HaveConversion<CouponIdConverter>();
        configurationBuilder.Properties<ReturnRequestId>().HaveConversion<ReturnRequestIdConverter>();
        configurationBuilder.Properties<CustomerId>().HaveConversion<CustomerIdConverter>();
        configurationBuilder.Properties<ProductId>().HaveConversion<ProductIdConverter>();
        configurationBuilder.Properties<Quantity>().HaveConversion<QuantityConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
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
