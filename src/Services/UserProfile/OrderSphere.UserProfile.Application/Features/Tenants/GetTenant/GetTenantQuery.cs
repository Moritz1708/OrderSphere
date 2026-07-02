namespace OrderSphere.UserProfile.Application.Features.Tenants.GetTenant;

public sealed record GetTenantQuery(Guid Id) : IQuery<Result<TenantDto>>;

public sealed class GetTenantQueryHandler(IUserProfileDbContext context)
    : IQueryHandler<GetTenantQuery, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> Handle(GetTenantQuery request, CancellationToken ct)
    {
        var tenant = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == TenantAggregateId.From(request.Id), ct);

        return tenant is null
            ? Result<TenantDto>.Failure(UserProfileErrors.TenantNotFound)
            : Result<TenantDto>.Success(TenantMappers.ToDto(tenant));
    }
}
