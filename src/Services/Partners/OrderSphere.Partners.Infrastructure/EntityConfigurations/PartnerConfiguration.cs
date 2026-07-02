using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSphere.Partners.Domain.Entities;

namespace OrderSphere.Partners.Infrastructure.EntityConfigurations;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("partners");

        builder.HasKey(p => p.Id);
        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.HasIndex(p => p.ApiKeyHash).IsUnique();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Property(p => p.QuotaTier).HasConversion<int>().IsRequired();
        builder.Property(p => p.ApiKeyHash).IsRequired().HasMaxLength(64);
        builder.Property(p => p.KeyPrefix).IsRequired().HasMaxLength(12);
    }
}
