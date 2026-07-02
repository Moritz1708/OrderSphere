using System.Net.Http.Json;

namespace OrderSphere.ApiGateway.Authentication;

public sealed class HttpPartnerLookupClient(
    HttpClient httpClient,
    ILogger<HttpPartnerLookupClient> logger) : IPartnerLookupClient
{
    public async Task<PartnerLookupResult?> FindByKeyHashAsync(string keyHash, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.GetAsync($"/internal/partners/by-key/{keyHash}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<PartnerLookupResult>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to look up partner for the supplied API key.");
            return null;
        }
    }
}
