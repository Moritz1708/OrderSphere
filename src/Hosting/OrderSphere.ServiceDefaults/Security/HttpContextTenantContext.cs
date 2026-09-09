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
        _claimTenantId = TenantClaimResolver.Resolve(httpContextAccessor.HttpContext?.User);
    }

    /// <summary>
    /// The ambient scope wins. It is opened by
    /// <c>RequestContextEnrichmentMiddleware</c> on the HTTP path and by
    /// <c>MessageProcessingScope.SetTenant</c> on the worker path, so one registration serves
    /// requests and background message processing alike (e.g. Invoicing.Api's embedded consumer).
    /// <para>
    /// On any request that reaches the middleware the ambient value and
    /// <c>_claimTenantId</c> are derived from the same claim by the same function and are
    /// therefore equal; the claim fallback stays as defence in depth for paths that bypass the
    /// enrichment branch (<c>/health</c>, <c>/alive</c>, <c>/version</c>). Removing it would make
    /// tenant isolation depend on middleware ordering in every host's <c>Program.cs</c>, where
    /// the failure mode — everything silently reading and writing the default tenant — is
    /// invisible until data has already crossed a boundary.
    /// </para>
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
