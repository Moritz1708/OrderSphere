using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.UserProfile.Domain.Enums;

namespace OrderSphere.UserProfile.Domain.Entities;

/// <summary>
/// Org-level tenant record (ADR 0012). <see cref="AuditableEntity{TId}.Id"/>'s underlying
/// <see cref="Guid"/> is the same value stamped as the row-scoping <c>TenantId</c> on every
/// tenant-owned entity across all services once the tenant is provisioned in Auth0 Organizations.
/// </summary>
public sealed class Tenant : AuditableEntity<TenantAggregateId>, IAggregateRoot
{
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public TenantStatus Status { get; private set; }

    private Tenant()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    public Tenant(string name, string slug)
    {
        Id = TenantAggregateId.New();
        Name = name;
        Slug = slug;
        Status = TenantStatus.Active;
    }

    public void Suspend() => Status = TenantStatus.Suspended;

    public void Reactivate() => Status = TenantStatus.Active;
}
