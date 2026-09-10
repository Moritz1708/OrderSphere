using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Catalog.Api.Configuration;
using OrderSphere.Catalog.Application.Abstractions;
using OrderSphere.Catalog.Application.Features.Products.Public.GetProductBySlug;
using OrderSphere.Catalog.Application.Features.Products.Public.GetProducts;
using OrderSphere.Catalog.Application.Features.Products.Public.GetSimilarProducts;
using OrderSphere.ServiceDefaults;

namespace OrderSphere.Catalog.Api.Endpoints.Public;

public static class ProductEndpoints
{
    public static void MapPublicProductEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetProducts)
            .WithName("GetProducts")
            .WithTags("Products");

        group.MapGet("/batch", GetProductsByIds)
            .WithName("GetProductsByIds")
            .WithTags("Products");

        group.MapGet("/{slug}", GetProductBySlug)
            .WithName("GetProductBySlug")
            .WithTags("Products");

        group.MapGet("/{slug}/similar", GetSimilarProducts)
            .WithName("GetSimilarProducts")
            .WithTags("Products");

        // Stock mutations share the public route prefix for historical reasons but are an
        // admin/system operation. Anonymous browsing made the previous open access visible.
        group.MapPost("/{id:guid}/stock/decrement", DecrementStock)
            .WithName("DecrementStock")
            .WithTags("Products")
            .RequireAuthorization(AuthorizationExtensions.CatalogAdminPolicy);

        group.MapPost("/{id:guid}/stock/restore", RestoreStock)
            .WithName("RestoreStock")
            .WithTags("Products")
            .RequireAuthorization(AuthorizationExtensions.CatalogAdminPolicy);
    }

    private static async Task<IResult> GetProducts(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? searchTerm,
        [FromQuery] string? categoryName,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        IMediator mediator,
        CancellationToken ct)
    {
        // The public sort contract is lowercase ("name"/"price"/"newest", "asc"/"desc"); enum
        // query binding is case-sensitive, so parse case-insensitively and fall back to defaults.
        var sort = Enum.TryParse<ProductSortBy>(sortBy, ignoreCase: true, out var sb)
            ? sb : ProductSortBy.Name;
        var direction = Enum.TryParse<SortDirection>(sortDir, ignoreCase: true, out var sd)
            ? sd : SortDirection.Asc;

        var result = await mediator.Send(
            new GetProductsQuery(
                page == 0 ? 1 : page,
                pageSize == 0 ? 20 : pageSize,
                searchTerm,
                categoryName,
                minPrice,
                maxPrice,
                sort,
                direction), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetProductsByIds(
        [FromQuery] string ids,
        IMediator mediator,
        CancellationToken ct)
    {
        var productIds = ids.Split(',')
            .Select(s => Guid.TryParse(s.Trim(), out var g) ? (Guid?)g : null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        var result = await mediator.Send(new GetProductsQuery(1, int.MaxValue), ct);
        if (result.IsFailure) return result.ToHttpResult();

        var filtered = result.Value.Items.Where(p => productIds.Contains(p.Id));
        return Results.Ok(filtered);
    }

    private static async Task<IResult> GetProductBySlug(
        string slug, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductBySlugQuery(slug), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSimilarProducts(
        string slug, [FromQuery] int limit, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSimilarProductsQuery(slug, limit == 0 ? 5 : limit), ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DecrementStock(
        Guid id, [FromBody] StockChangeRequest body, ICatalogDbContext context, CancellationToken ct)
    {
        var product = await context.Products
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == ProductId.From(id), ct);

        if (product is null) return Results.NotFound();

        var result = product.RemoveFromStock(body.Quantity);
        if (result.IsFailure) return result.ToHttpResult();

        await context.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreStock(
        Guid id, [FromBody] StockChangeRequest body, ICatalogDbContext context, CancellationToken ct)
    {
        var product = await context.Products
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == ProductId.From(id), ct);

        if (product is null) return Results.NotFound();

        product.AddToStock(body.Quantity);
        await context.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private sealed record StockChangeRequest(int Quantity);
}
