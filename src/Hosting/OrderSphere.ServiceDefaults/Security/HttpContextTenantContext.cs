using Microsoft.AspNetCore.Http;
using OrderSphere.BuildingBlocks.Security;
using TenantIdHelper = OrderSphere.BuildingBlocks.StronglyTypedIds.TenantId;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Resolves <see cref="ITenantContext"/> from the ambient <see cref="IHttpContextAccessor"/>'s
/// Auth0 <c>org_id</c> claim (ADR 0012). Registered as <em>scoped</em> so it is fresh per request.
/// </summary>
internal sealed class HttpContextTenantContext : ITenantContext
{
    private readonly Guid? _claimTenantId;

    public HttpContextTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var orgId = httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value;
        _claimTenantId = orgId is null ? null : TenantIdHelper.FromOrgId(orgId);
    }

    /// <summary>
    /// Ambient scope (set explicitly by worker message loops, see <see cref="AmbientTenantContext"/>)
    /// takes precedence over the request claim so the same registration serves both API requests
    /// and background message processing within the same process (e.g. Invoicing.Api's embedded
    /// consumer).
    /// </summary>
    public Guid TenantId => AmbientTenantContext.Ambient ?? _claimTenantId ?? TenantIdHelper.Default;
}

/// <summary>
/// Extension method to register <see cref="ITenantContext"/> in the DI container.
/// Call from each API's composition root after calling <c>AddOrderSphereJwtAuth</c>.
/// </summary>
public static class TenantContextExtensions
{
    /// <summary>
    /// Registers <see cref="ITenantContext"/> as a scoped service backed by
    /// <see cref="HttpContextTenantContext"/>.
    /// </summary>
    public static IServiceCollection AddTenantContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpContextTenantContext>();
        return services;
    }
}
