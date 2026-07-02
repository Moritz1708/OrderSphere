namespace OrderSphere.Partners.Api.Configuration;

/// <summary>
/// Authorization policies for the Partners API.
/// <see cref="AdminPolicy"/> requires the <c>admin</c> role and guards every partner
/// management endpoint (create, list, rotate, revoke) — B2B partners never call this API
/// directly, only the ApiGateway calls the internal by-key lookup.
/// </summary>
public static class AuthorizationExtensions
{
    public const string AdminPolicy = "AdminPolicy";

    public static IServiceCollection AddPartnersAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("admin"));

        return services;
    }
}
