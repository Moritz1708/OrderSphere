using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSphere.BuildingBlocks.EventBus.Outbox;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Outbox;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.RetryCount).HasDefaultValue(0);
        // W3C traceparent is a fixed 55-char string ("00-" + 32 + "-" + 16 + "-" + 2).
        builder.Property(x => x.TraceParent).HasMaxLength(55);
        // Not 55: unlike traceparent this can carry a client-supplied X-Request-Id.
        builder.Property(x => x.CorrelationId).HasMaxLength(OutboxMessage.MaxCorrelationIdLength);
        // Composite index optimises the dispatcher query: WHERE ProcessedAt IS NULL ORDER BY OccurredAt
        builder.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
        builder.HasIndex(x => x.RetryCount);
    }
}
