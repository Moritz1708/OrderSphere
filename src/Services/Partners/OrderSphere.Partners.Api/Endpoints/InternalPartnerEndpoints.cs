using MediatR;
using OrderSphere.Partners.Application.Features.Partners.GetPartnerByKeyHash;
using OrderSphere.ServiceDefaults;

namespace OrderSphere.Partners.Api.Endpoints;

/// <summary>
/// Service-internal endpoint not exposed through the API gateway. Called by the
/// ApiGateway's <c>ApiKeyAuthenticationHandler</c> (B6) to resolve a pre-hashed
/// <c>X-API-Key</c> value to a partner.
/// </summary>
public static class InternalPartnerEndpoints
{
    public static void MapInternalPartnerEndpoints(this WebApplication app)
    {
        // D4 — requires a valid client-credentials token (any authenticated caller); M2M
        // tokens carry no role claims, so no role-based policy is applied here.
        var group = app.MapGroup("internal/partners").RequireAuthorization();

        group.MapGet("by-key/{hash}",
            async (string hash, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetPartnerByKeyHashQuery(hash), ct);
                return result.ToHttpResult();
            });
    }
}
