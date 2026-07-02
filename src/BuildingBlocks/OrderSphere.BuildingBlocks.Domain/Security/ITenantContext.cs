namespace OrderSphere.BuildingBlocks.Security;

/// <summary>
/// Provides the tenant (organisation) the current request or message belongs to (ADR 0012).
/// Backed by <c>HttpContextTenantContext</c> in API processes and by an <c>AsyncLocal</c>-based
/// ambient context in workers, which have no <see cref="Microsoft.AspNetCore.Http.HttpContext"/>
/// and must set it explicitly from the consumed integration event before persisting.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Resolved tenant for the current scope. Defaults to <c>TenantId.Default</c>
    /// (<see cref="Guid.Empty"/>) when no organisation claim/context is present.
    /// </summary>
    Guid TenantId { get; }
}
