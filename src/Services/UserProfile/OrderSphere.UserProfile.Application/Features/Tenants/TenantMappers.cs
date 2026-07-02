namespace OrderSphere.UserProfile.Application.Features.Tenants;

internal static class TenantMappers
{
    public static TenantDto ToDto(Tenant tenant) =>
        new(tenant.Id.Value, tenant.Name, tenant.Slug, tenant.Status.ToString(), tenant.CreatedAt);
}
