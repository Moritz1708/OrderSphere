using System.Security.Claims;
using TenantIdHelper = OrderSphere.BuildingBlocks.StronglyTypedIds.TenantId;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Derives the tenant from a principal's Auth0 Organizations <c>org_id</c> claim (ADR 0012).
/// <para>
/// Shared by <see cref="HttpContextTenantContext"/> (which backs <c>ITenantContext</c> for EF
/// stamping and query filtering) and by the request pipeline, which opens the ambient tenant
/// scope. One implementation so the two cannot disagree about what the claim means.
/// </para>
/// </summary>
internal static class TenantClaimResolver
{
    /// <summary>Auth0 Organizations claim carrying the org identifier (ADR 0012).</summary>
    internal const string OrgIdClaim = "org_id";

    /// <summary>
    /// Returns the tenant for <paramref name="user"/>, or <see langword="null"/> when the
    /// principal is anonymous or carries no usable <c>org_id</c>.
    /// <para>
    /// Null rather than <c>TenantId.Default</c> on purpose: callers must be able to tell "no
    /// tenant" from "the default tenant". The log pipeline omits <c>tenant_id</c> entirely in the
    /// first case, which is honest; stamping an all-zero GUID on anonymous traffic would be
    /// indistinguishable from a genuine single-organisation deployment.
    /// </para>
    /// <para>
    /// The whitespace guard matters: <see cref="TenantIdHelper.FromOrgId"/> is total — it hashes
    /// whatever it is given, so an empty claim value would otherwise derive a valid-looking but
    /// entirely fictional tenant rather than falling back.
    /// </para>
    /// </summary>
    internal static Guid? Resolve(ClaimsPrincipal? user)
    {
        var orgId = user?.FindFirst(OrgIdClaim)?.Value;
        return string.IsNullOrWhiteSpace(orgId) ? null : TenantIdHelper.FromOrgId(orgId);
    }
}
