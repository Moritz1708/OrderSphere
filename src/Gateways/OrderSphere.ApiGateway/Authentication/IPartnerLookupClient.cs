namespace OrderSphere.ApiGateway.Authentication;

public interface IPartnerLookupClient
{
    /// <summary>Returns the partner for an already-hashed API key, or null if unknown/revoked.</summary>
    Task<PartnerLookupResult?> FindByKeyHashAsync(string keyHash, CancellationToken ct);
}

public sealed record PartnerLookupResult(Guid PartnerId, string Name, string Status, string QuotaTier);
