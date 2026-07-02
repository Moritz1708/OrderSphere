namespace OrderSphere.Partners.Application.Models;

public sealed record PartnerDto(
    Guid Id,
    string Name,
    string Status,
    string QuotaTier,
    string KeyPrefix,
    DateTime? LastRotatedAt,
    DateTime CreatedAt);

/// <summary>Returned once, at creation or rotation time — the raw key cannot be recovered afterward.</summary>
public sealed record PartnerApiKeyDto(Guid PartnerId, string ApiKey);

public sealed record CreatePartnerRequest(string Name, string QuotaTier);

/// <summary>
/// Internal lookup result consumed by the ApiGateway's <c>ApiKeyAuthenticationHandler</c>
/// (B6) — never exposed through the versioned public API.
/// </summary>
public sealed record PartnerLookupDto(Guid PartnerId, string Name, string Status, string QuotaTier);
