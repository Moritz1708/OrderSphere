using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.Ordering.Application.Abstractions;

namespace OrderSphere.Ordering.Infrastructure.CatalogClient;

public sealed class HttpBasketClient(HttpClient httpClient, ILogger<HttpBasketClient> logger) : IBasketClient
{
    public async Task<Result<BasketCartInfo>> GetCartAsync(Guid customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"/internal/cart/{customerId}", ct);
            if (!response.IsSuccessStatusCode)
                return Result<BasketCartInfo>.Failure(new Error("Basket.CartNotFound", "Cart not found."));

            var dto = await response.Content.ReadFromJsonAsync<BasketCartInfo>(ct);
            return dto is not null
                ? Result<BasketCartInfo>.Success(dto)
                : Result<BasketCartInfo>.Failure(new Error("Basket.CartNotFound", "Cart not found."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching cart for customer {CustomerId} from Basket", customerId);
            return Result<BasketCartInfo>.Failure(new Error("Basket.Unavailable", "Basket service unavailable."));
        }
    }

    public async Task<Result> ClearCartItemsAsync(Guid customerId, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"/internal/cart/{customerId}/items", ct);
            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(new Error("Basket.ClearFailed", "Failed to clear cart items."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error clearing cart items for customer {CustomerId}", customerId);
            return Result.Failure(new Error("Basket.Unavailable", "Basket service unavailable."));
        }
    }
}
