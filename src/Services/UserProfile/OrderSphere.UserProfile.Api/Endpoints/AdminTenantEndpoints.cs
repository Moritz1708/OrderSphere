using MediatR;
using OrderSphere.ServiceDefaults;
using OrderSphere.UserProfile.Application.Features.Tenants.CreateTenant;
using OrderSphere.UserProfile.Application.Features.Tenants.GetAllTenants;
using OrderSphere.UserProfile.Application.Features.Tenants.GetTenant;
using OrderSphere.UserProfile.Application.Models;

namespace OrderSphere.UserProfile.Api.Endpoints;

public static class AdminTenantEndpoints
{
    public static void MapAdminTenantEndpoints(this RouteGroupBuilder v1)
    {
        var admin = v1.MapGroup("admin/tenants").RequireAuthorization("AdminPolicy");

        admin.MapGet("/", GetAllTenants);
        admin.MapGet("/{id:guid}", GetTenant);
        admin.MapPost("/", CreateTenant);
    }

    private static async Task<IResult> GetAllTenants(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAllTenantsQuery(), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetTenant(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetTenantQuery(id), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateTenant(
        CreateTenantRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateTenantCommand(request.Name, request.Slug), ct);
        return result.ToHttpResult();
    }
}
