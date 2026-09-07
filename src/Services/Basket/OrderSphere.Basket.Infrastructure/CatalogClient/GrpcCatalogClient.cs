using Grpc.Core;
using Microsoft.Extensions.Logging;
using OrderSphere.Basket.Application.Abstractions;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.Catalog.V1;

namespace OrderSphere.Basket.Infrastructure.CatalogClient;

/// <summary>
/// gRPC-backed <see cref="ICatalogClient"/> for internal Basket→Catalog stock checks (A5).
/// Transport resilience and service discovery are applied to the generated channel by the
/// global HttpClient defaults in ServiceDefaults. App-level outcomes (product not found,
/// transport unavailable) are surfaced as <see cref="Result{T}"/> values, never exceptions.
/// </summary>
public sealed class GrpcCatalogClient(
    CatalogService.CatalogServiceClient client,
    ILogger<GrpcCatalogClient> logger) : ICatalogClient
{
    public async Task<Result<CatalogProductInfo>> GetProductByIdAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            var response = await client.GetProductAsync(
                new GetProductRequest { ProductId = productId.ToString() }, cancellationToken: ct);

            return response.Found
                ? Result<CatalogProductInfo>.Success(Map(response))
                : Result<CatalogProductInfo>.Failure(new Error("Catalog.ProductNotFound", "Product not found."));
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "gRPC error fetching product {ProductId} from Catalog", productId);
            return Result<CatalogProductInfo>.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }

    public async Task<Result<IReadOnlyDictionary<Guid, CatalogProductInfo>>> GetProductInfosByIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        var ids = productIds.ToList();
        if (ids.Count == 0)
            return Result<IReadOnlyDictionary<Guid, CatalogProductInfo>>.Success(
                new Dictionary<Guid, CatalogProductInfo>());

        try
        {
            var request = new GetProductsRequest();
            request.ProductIds.AddRange(ids.Select(id => id.ToString()));

            var response = await client.GetProductsAsync(request, cancellationToken: ct);

            var dict = response.Products
                .Where(p => p.Found && Guid.TryParse(p.Id, out _))
                .ToDictionary(p => Guid.Parse(p.Id), Map);

            return Result<IReadOnlyDictionary<Guid, CatalogProductInfo>>.Success(dict);
        }
        catch (RpcException ex)
        {
            // Cart enrichment degrades gracefully: an unreachable Catalog yields an empty map,
            // so the cart still renders (names/prices fall back to placeholders).
            logger.LogWarning(ex, "gRPC error fetching product infos from Catalog. Continuing without enrichment.");
            return Result<IReadOnlyDictionary<Guid, CatalogProductInfo>>.Success(
                new Dictionary<Guid, CatalogProductInfo>());
        }
    }

    private static CatalogProductInfo Map(GetProductResponse p) =>
        new(Guid.Parse(p.Id), p.Name, (decimal)p.Price, p.Stock, p.IsActive);
}
