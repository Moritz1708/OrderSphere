using OrderSphere.Partners.Domain.Enums;

namespace OrderSphere.Partners.Application.Features.Partners.GetPartnerByKeyHash;

/// <summary>
/// Backs the internal <c>GET /internal/partners/by-key/{hash}</c> endpoint that the
/// ApiGateway's <c>ApiKeyAuthenticationHandler</c> calls to resolve an <c>X-API-Key</c>
/// header (already SHA-256 hashed by the caller) to a partner. Active partners only —
/// a revoked key resolves to <see cref="PartnerErrors.NotFound"/>, matching how invalid
/// keys behave, so callers cannot distinguish "unknown key" from "revoked key".
/// </summary>
public sealed record GetPartnerByKeyHashQuery(string ApiKeyHash) : IQuery<Result<PartnerLookupDto>>;

public sealed class GetPartnerByKeyHashQueryHandler(IPartnersDbContext context)
    : IQueryHandler<GetPartnerByKeyHashQuery, Result<PartnerLookupDto>>
{
    public async Task<Result<PartnerLookupDto>> Handle(GetPartnerByKeyHashQuery request, CancellationToken ct)
    {
        var partner = await context.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.ApiKeyHash == request.ApiKeyHash && p.Status == PartnerStatus.Active, ct);

        return partner is null
            ? Result<PartnerLookupDto>.Failure(PartnerErrors.NotFound)
            : Result<PartnerLookupDto>.Success(PartnerMappers.ToLookupDto(partner));
    }
}
