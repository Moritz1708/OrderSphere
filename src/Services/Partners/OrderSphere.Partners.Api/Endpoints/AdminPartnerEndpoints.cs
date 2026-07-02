using MediatR;
using OrderSphere.Partners.Api.Configuration;
using OrderSphere.Partners.Application.Features.Partners.CreatePartner;
using OrderSphere.Partners.Application.Features.Partners.GetAllPartners;
using OrderSphere.Partners.Application.Features.Partners.GetPartner;
using OrderSphere.Partners.Application.Features.Partners.RevokeApiKey;
using OrderSphere.Partners.Application.Features.Partners.RotateApiKey;
using OrderSphere.Partners.Application.Models;
using OrderSphere.Partners.Domain.Enums;
using OrderSphere.ServiceDefaults;

namespace OrderSphere.Partners.Api.Endpoints;

public static class AdminPartnerEndpoints
{
    public static void MapAdminPartnerEndpoints(this RouteGroupBuilder v1)
    {
        var admin = v1.MapGroup("admin/partners").RequireAuthorization(AuthorizationExtensions.AdminPolicy);

        admin.MapGet("/", GetAllPartners);
        admin.MapGet("/{id:guid}", GetPartner);
        admin.MapPost("/", CreatePartner);
        admin.MapPost("/{id:guid}/rotate-key", RotateApiKey);
        admin.MapPost("/{id:guid}/revoke", RevokeApiKey);
    }

    private static async Task<IResult> GetAllPartners(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAllPartnersQuery(), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetPartner(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetPartnerQuery(id), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreatePartner(
        CreatePartnerRequest request, ISender sender, CancellationToken ct)
    {
        if (!Enum.TryParse<QuotaTier>(request.QuotaTier, ignoreCase: true, out var quotaTier))
            return Results.BadRequest($"Unknown quota tier '{request.QuotaTier}'.");

        var result = await sender.Send(new CreatePartnerCommand(request.Name, quotaTier), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RotateApiKey(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RotateApiKeyCommand(id), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeApiKey(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RevokeApiKeyCommand(id), ct);
        return result.ToHttpResult();
    }
}
