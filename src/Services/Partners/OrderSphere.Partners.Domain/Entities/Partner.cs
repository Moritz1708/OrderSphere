using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Partners.Domain.Enums;
using OrderSphere.Partners.Domain.Errors;

namespace OrderSphere.Partners.Domain.Entities;

/// <summary>
/// A B2B partner authenticating at the ApiGateway via <c>X-API-Key</c> (B6). Only the SHA-256
/// hash of the active key is stored; the raw key is shown to the caller once, at issue or
/// rotation time. <see cref="KeyPrefix"/> is a short, non-secret slice of the raw key kept for
/// display/audit purposes (e.g. "ps_A1B2C3D4...").
/// </summary>
public sealed class Partner : AuditableEntity<PartnerId>, IAggregateRoot
{
    public string Name { get; private set; }
    public PartnerStatus Status { get; private set; }
    public QuotaTier QuotaTier { get; private set; }
    public string ApiKeyHash { get; private set; }
    public string KeyPrefix { get; private set; }
    public DateTime? LastRotatedAt { get; private set; }

    private Partner()
    {
        Name = string.Empty;
        ApiKeyHash = string.Empty;
        KeyPrefix = string.Empty;
    }

    private Partner(string name, QuotaTier quotaTier)
    {
        Id = PartnerId.New();
        Name = name;
        QuotaTier = quotaTier;
        Status = PartnerStatus.Active;
        ApiKeyHash = string.Empty;
        KeyPrefix = string.Empty;
    }

    /// <summary>Creates a new partner and issues its initial API key in one step.</summary>
    public static (Partner Partner, string ApiKey) Create(string name, QuotaTier quotaTier)
    {
        var partner = new Partner(name, quotaTier);
        var apiKey = partner.IssueApiKey();
        return (partner, apiKey);
    }

    public Result<string> RotateApiKey()
    {
        if (Status == PartnerStatus.Revoked)
            return Result<string>.Failure(PartnerErrors.AlreadyRevoked);

        return Result<string>.Success(IssueApiKey());
    }

    public Result Revoke()
    {
        if (Status == PartnerStatus.Revoked)
            return Result.Failure(PartnerErrors.AlreadyRevoked);

        Status = PartnerStatus.Revoked;
        return Result.Success();
    }

    private string IssueApiKey()
    {
        var rawKey = ApiKeyGenerator.Generate();
        ApiKeyHash = ApiKeyGenerator.Hash(rawKey);
        KeyPrefix = rawKey[..12];
        LastRotatedAt = DateTime.UtcNow;
        return rawKey;
    }
}
