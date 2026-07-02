using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSphere.UserProfile.Domain.Entities;

namespace OrderSphere.UserProfile.Infrastructure.EntityConfigurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.HasQueryFilter(t => !t.IsDeleted);

        builder.HasIndex(t => t.Slug).IsUnique();

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Slug).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
    }
}
