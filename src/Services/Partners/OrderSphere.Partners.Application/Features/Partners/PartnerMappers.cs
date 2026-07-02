using OrderSphere.Partners.Domain.Entities;

namespace OrderSphere.Partners.Application.Features.Partners;

internal static class PartnerMappers
{
    public static PartnerDto ToDto(Partner partner) => new(
        partner.Id.Value,
        partner.Name,
        partner.Status.ToString(),
        partner.QuotaTier.ToString(),
        partner.KeyPrefix,
        partner.LastRotatedAt,
        partner.CreatedAt);

    public static PartnerLookupDto ToLookupDto(Partner partner) => new(
        partner.Id.Value,
        partner.Name,
        partner.Status.ToString(),
        partner.QuotaTier.ToString());
}
