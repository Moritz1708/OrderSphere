using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSphere.Payment.Domain.Entities;

namespace OrderSphere.Payment.Infrastructure.EntityConfigurations;

internal sealed class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.CorrelationId);
        // Stripe webhooks for charges reference only the PaymentIntent id.
        builder.HasIndex(p => p.TransactionId);

        // Money mapped onto the existing "Amount"/"Currency" columns — no schema change.
        builder.ComplexProperty(p => p.Amount, b =>
        {
            b.Property(m => m.Amount)
             .HasColumnName("Amount")
             .HasPrecision(18, 2)
             .IsRequired();
            b.Property(m => m.Currency)
             .HasColumnName("Currency")
             .HasMaxLength(3)
             .IsRequired();
        });
        builder.Property(p => p.PaymentMethod).HasMaxLength(50);
        builder.Property(p => p.CustomerEmail).HasMaxLength(256);
        builder.Property(p => p.TransactionId).HasMaxLength(256);
        builder.Property(p => p.FailureReason).HasMaxLength(1024);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
    }
}
