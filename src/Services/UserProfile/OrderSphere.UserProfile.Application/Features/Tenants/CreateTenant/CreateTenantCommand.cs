namespace OrderSphere.UserProfile.Application.Features.Tenants.CreateTenant;

/// <summary>
/// Onboards a new org-level tenant (ADR 0012). The resulting <c>Tenant.Id</c> is the Guid to
/// provision as the Auth0 Organization and to derive via <c>TenantId.FromOrgId</c> at runtime.
/// </summary>
public sealed record CreateTenantCommand(string Name, string Slug) : ICommand<Result<TenantDto>>;

public sealed class CreateTenantCommandHandler(IUserProfileDbContext context)
    : ICommandHandler<CreateTenantCommand, Result<TenantDto>>
{
    public async Task<Result<TenantDto>> Handle(CreateTenantCommand request, CancellationToken ct)
    {
        var slugTaken = await context.Tenants.AnyAsync(t => t.Slug == request.Slug, ct);
        if (slugTaken)
            return Result<TenantDto>.Failure(UserProfileErrors.TenantSlugTaken);

        var tenant = new Tenant(request.Name, request.Slug);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(ct);

        return Result<TenantDto>.Success(TenantMappers.ToDto(tenant));
    }
}
