using Microsoft.Extensions.Caching.Hybrid;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Catalog.Application.Diagnostics;

namespace OrderSphere.Catalog.Application.Features.Products.Public.GetProductBySlug;

public sealed class GetProductBySlugQueryHandler(ICatalogDbContext context, HybridCache cache, ITenantContext tenant)
    : IQueryHandler<GetProductBySlugQuery, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(GetProductBySlugQuery request, CancellationToken ct)
    {
        var miss = false;
        var dto = await cache.GetOrCreateAsync(
            CatalogCache.ProductBySlugKey(tenant.TenantId, request.Slug),
            async token =>
            {
                miss = true;
                return await FetchAsync(request.Slug, token);
            },
            tags: [CatalogCache.Tag],
            cancellationToken: ct);

        // The factory runs only on a miss; everything else was served from the cache.
        if (miss)
            CatalogMetrics.CacheMisses.Add(1);
        else
            CatalogMetrics.CacheHits.Add(1);

        return dto is null
            ? Result<ProductDto>.Failure(ProductErrors.NotFound)
            : Result<ProductDto>.Success(dto);
    }

    private async Task<ProductDto?> FetchAsync(string slug, CancellationToken ct) =>
        await context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.IsActive)
            .Select(p => new ProductDto(
                p.Id.Value,
                p.Name,
                p.Slug,
                p.Description,
                p.Price,
                p.Stock,
                p.CategoryId.Value,
                p.Category!.Name,
                p.SKU,
                p.ImageUrl,
                p.IsActive,
                p.AverageRating,
                p.ReviewCount,
                p.Brand != null ? (Guid?)p.Brand.Id.Value : null,
                p.Brand != null ? p.Brand.Name : null))
            .FirstOrDefaultAsync(ct);
}
