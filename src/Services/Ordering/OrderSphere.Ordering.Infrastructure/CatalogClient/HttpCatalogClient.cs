using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.Ordering.Application.Abstractions;

namespace OrderSphere.Ordering.Infrastructure.CatalogClient;

public sealed class HttpCatalogClient(HttpClient httpClient, ILogger<HttpCatalogClient> logger) : ICatalogClient
{
    public async Task<Result<CatalogProductInfo>> GetProductByIdAsync(Guid productId, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"/internal/products/{productId}", ct);
            if (!response.IsSuccessStatusCode)
                return Result<CatalogProductInfo>.Failure(new Error("Catalog.ProductNotFound", "Product not found."));

            var dto = await response.Content.ReadFromJsonAsync<CatalogProductInfo>(ct);
            return dto is not null
                ? Result<CatalogProductInfo>.Success(dto)
                : Result<CatalogProductInfo>.Failure(new Error("Catalog.ProductNotFound", "Product not found."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching product {ProductId} from Catalog", productId);
            return Result<CatalogProductInfo>.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }

    public async Task<Result<IReadOnlyDictionary<Guid, string>>> GetProductNamesByIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken ct = default)
    {
        try
        {
            var ids = productIds.ToList();
            if (ids.Count == 0)
                return Result<IReadOnlyDictionary<Guid, string>>.Success(new Dictionary<Guid, string>());

            var query = string.Join("&", ids.Select(id => $"ids={id}"));
            var response = await httpClient.GetAsync($"/internal/products/names?{query}", ct);

            if (!response.IsSuccessStatusCode)
                return Result<IReadOnlyDictionary<Guid, string>>.Success(new Dictionary<Guid, string>());

            var dict = await response.Content.ReadFromJsonAsync<Dictionary<Guid, string>>(ct);
            return Result<IReadOnlyDictionary<Guid, string>>.Success(
                dict ?? new Dictionary<Guid, string>());
        }
        catch (Exception ex)
        {
            // Product names are presentational only, so an unreachable Catalog degrades to
            // "no names" rather than failing the caller — hence Success with an empty map.
            // The log record is the only trace of the degradation, which is why it is a
            // Warning and not swallowed silently.
            logger.LogWarning(ex, "Error fetching product names from Catalog. Continuing without names.");
            return Result<IReadOnlyDictionary<Guid, string>>.Success(new Dictionary<Guid, string>());
        }
    }

    public async Task<Result> DecrementStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"/internal/products/{productId}/decrement-stock",
                new { Quantity = quantity }, ct);

            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(new Error("Catalog.StockDecrement", "Stock decrement failed."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error decrementing stock for product {ProductId}", productId);
            return Result.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }

    public async Task<Result> RestoreStockAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"/internal/products/{productId}/restore-stock",
                new { Quantity = quantity }, ct);

            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(new Error("Catalog.StockRestore", "Stock restore failed."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error restoring stock for product {ProductId}", productId);
            return Result.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }

    public async Task<Result> ReserveStockAsync(
        Guid correlationId, IReadOnlyList<ReservationItem> items, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                "/internal/reservations",
                new
                {
                    CorrelationId = correlationId,
                    Items = items.Select(i => new { i.ProductId, i.Quantity }),
                }, ct);

            if (response.IsSuccessStatusCode)
                return Result.Success();

            return response.StatusCode == System.Net.HttpStatusCode.Conflict
                ? Result.Failure(new Error("Catalog.InsufficientStock", "Insufficient stock to reserve.", ErrorType.Conflict))
                : Result.Failure(new Error("Catalog.Reserve", "Stock reservation failed."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error reserving stock for correlation {CorrelationId}", correlationId);
            return Result.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }

    public async Task<Result> ConfirmReservationAsync(Guid correlationId, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.PostAsync($"/internal/reservations/{correlationId}/confirm", null, ct);
            if (response.IsSuccessStatusCode)
                return Result.Success();

            // A 409 is a genuine, non-recoverable business conflict (on-hand stock can no longer
            // cover the reservation): retrying cannot succeed, so the caller compensates. Any other
            // status (5xx, 408, …) is transient and must be retried — it must never be mistaken for
            // a confirm that "can't succeed", which would refund an already-captured payment.
            return response.StatusCode == System.Net.HttpStatusCode.Conflict
                ? Result.Failure(new Error("Catalog.ConfirmReservation", "Reservation confirm conflict.", ErrorType.Conflict))
                : Result.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error confirming reservation for correlation {CorrelationId}", correlationId);
            return Result.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }

    public async Task<Result> ReleaseReservationAsync(Guid correlationId, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.PostAsync($"/internal/reservations/{correlationId}/release", null, ct);
            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(new Error("Catalog.ReleaseReservation", "Reservation release failed."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error releasing reservation for correlation {CorrelationId}", correlationId);
            return Result.Failure(new Error("Catalog.Unavailable", "Catalog service unavailable."));
        }
    }
}
