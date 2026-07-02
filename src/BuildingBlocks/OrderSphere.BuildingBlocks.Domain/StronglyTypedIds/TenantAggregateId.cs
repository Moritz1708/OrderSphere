namespace OrderSphere.BuildingBlocks.StronglyTypedIds;

/// <summary>
/// Primary key of the <c>Tenant</c> management aggregate (UserProfile service, ADR 0012).
/// Its <see cref="Value"/> is the same <see cref="Guid"/> stamped as the row-scoping
/// <c>TenantId</c> on every <c>AuditableEntity</c> across all services — see
/// <c>ITenantContext</c>/<c>TenantId.FromOrgId</c> for how callers derive it from a claim.
/// </summary>
public readonly record struct TenantAggregateId(Guid Value)
{
    public static TenantAggregateId New() => new(Guid.CreateVersion7());
    public static TenantAggregateId Empty => new(Guid.Empty);
    public static TenantAggregateId From(Guid v) => new(v);

    public override string ToString() => Value.ToString();
}
