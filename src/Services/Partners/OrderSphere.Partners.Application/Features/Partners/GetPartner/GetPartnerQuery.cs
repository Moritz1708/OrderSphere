namespace OrderSphere.Partners.Application.Features.Partners.GetPartner;

public sealed record GetPartnerQuery(Guid PartnerId) : IQuery<Result<PartnerDto>>;

public sealed class GetPartnerQueryHandler(IPartnersDbContext context)
    : IQueryHandler<GetPartnerQuery, Result<PartnerDto>>
{
    public async Task<Result<PartnerDto>> Handle(GetPartnerQuery request, CancellationToken ct)
    {
        var partner = await context.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id.Value == request.PartnerId, ct);

        return partner is null
            ? Result<PartnerDto>.Failure(PartnerErrors.NotFound)
            : Result<PartnerDto>.Success(PartnerMappers.ToDto(partner));
    }
}
