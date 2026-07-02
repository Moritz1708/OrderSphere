using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OrderSphere.BuildingBlocks.Abstraction;

namespace OrderSphere.BuildingBlocks.Extensions;

/// <summary>
/// Extension methods for <see cref="ChangeTracker"/> to automate audit field population.
/// Call <see cref="ApplyAuditFields"/> in each DbContext's <c>SaveChangesAsync</c> override
/// before delegating to <c>base.SaveChangesAsync</c>.
/// </summary>
public static class ChangeTrackerExtensions
{
    /// <summary>
    /// Sets <see cref="IAuditableEntity.CreatedAt"/>, <see cref="IAuditableEntity.TenantId"/>
    /// (and clears <see cref="IAuditableEntity.IsDeleted"/>) on Added entries, and sets
    /// <see cref="IAuditableEntity.UpdatedAt"/> on Modified entries.
    /// </summary>
    /// <param name="currentTenantId">
    /// The ambient tenant (ADR 0012), read from <c>ITenantContext.TenantId</c> at the DbContext's
    /// composition root. Only stamped on insert — an entity never changes tenant after creation.
    /// </param>
    public static void ApplyAuditFields(this ChangeTracker changeTracker, Guid currentTenantId)
    {
        foreach (var entry in changeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity is not IAuditableEntity auditable)
                continue;

            if (entry.State == EntityState.Added)
            {
                auditable.CreatedAt = DateTime.UtcNow;
                auditable.IsDeleted = false;
                auditable.TenantId = currentTenantId;
            }
            else
            {
                auditable.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
