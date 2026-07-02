namespace OrderSphere.BuildingBlocks.Abstraction;

/// <summary>
/// Marker interface for audit fields. Does not include Id — each entity
/// declares its primary key via <see cref="AuditableEntity{TId}"/>.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    bool IsDeleted { get; set; }

    /// <summary>
    /// Tenant that owns this row (see ADR 0012). Stamped automatically from the ambient
    /// <c>ITenantContext</c> on insert; enforced by the combined tenant/soft-delete query filter.
    /// </summary>
    Guid TenantId { get; set; }
}
