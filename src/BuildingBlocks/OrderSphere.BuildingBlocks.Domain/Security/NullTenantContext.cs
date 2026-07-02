using TenantIdHelper = OrderSphere.BuildingBlocks.StronglyTypedIds.TenantId;

namespace OrderSphere.BuildingBlocks.Security;

/// <summary>
/// No-op <see cref="ITenantContext"/> used exclusively by EF Core design-time factories
/// (<c>IDesignTimeDbContextFactory</c>) where a real DI container is unavailable.
/// Must not be registered in production DI.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    private NullTenantContext() { }

    public Guid TenantId => TenantIdHelper.Default;
}
