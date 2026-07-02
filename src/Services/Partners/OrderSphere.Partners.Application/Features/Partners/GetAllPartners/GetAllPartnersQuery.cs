namespace OrderSphere.Partners.Application.Features.Partners.GetAllPartners;

public sealed record GetAllPartnersQuery : IQuery<Result<IReadOnlyList<PartnerDto>>>;

public sealed class GetAllPartnersQueryHandler(IPartnersDbContext context)
    : IQueryHandler<GetAllPartnersQuery, Result<IReadOnlyList<PartnerDto>>>
{
    public async Task<Result<IReadOnlyList<PartnerDto>>> Handle(GetAllPartnersQuery request, CancellationToken ct)
    {
        var partners = await context.Partners
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => PartnerMappers.ToDto(p))
            .ToListAsync(ct);

        return Result<IReadOnlyList<PartnerDto>>.Success(partners);
    }
}
