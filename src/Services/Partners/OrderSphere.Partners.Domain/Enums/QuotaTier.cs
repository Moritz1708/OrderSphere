namespace OrderSphere.Partners.Domain.Enums;

/// <summary>
/// Per-minute request quota tier for B2B partners calling through the ApiGateway
/// (ADR-pending B6). The gateway's rate limiter maps this to a concrete permit count —
/// the Partners service only tracks which tier a partner is on.
/// </summary>
public enum QuotaTier
{
    Standard = 0,
    Premium = 1,
}
