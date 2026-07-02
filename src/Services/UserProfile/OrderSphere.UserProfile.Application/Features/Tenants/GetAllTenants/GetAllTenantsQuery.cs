namespace OrderSphere.UserProfile.Application.Features.Tenants.GetAllTenants;

public sealed record GetAllTenantsQuery : IQuery<Result<IReadOnlyList<TenantDto>>>;

public sealed class GetAllTenantsQueryHandler(IUserProfileDbContext context)
    : IQueryHandler<GetAllTenantsQuery, Result<IReadOnlyList<TenantDto>>>
{
    public async Task<Result<IReadOnlyList<TenantDto>>> Handle(GetAllTenantsQuery request, CancellationToken ct)
    {
        var tenants = await context.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TenantDto(t.Id.Value, t.Name, t.Slug, t.Status.ToString(), t.CreatedAt))
            .ToListAsync(ct);

        return Result<IReadOnlyList<TenantDto>>.Success(tenants);
    }
}
