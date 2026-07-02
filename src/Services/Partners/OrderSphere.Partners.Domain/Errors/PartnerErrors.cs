using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.Partners.Domain.Errors;

public static class PartnerErrors
{
    public static readonly Error UnknownError =
        new("Partner.Unknown", "An unexpected error occurred.", ErrorType.Unexpected);

    public static readonly Error NotFound =
        new("Partner.NotFound", "Partner was not found.", ErrorType.NotFound);

    public static readonly Error AlreadyRevoked =
        new("Partner.AlreadyRevoked", "The partner's API key has already been revoked.", ErrorType.Conflict);
}
