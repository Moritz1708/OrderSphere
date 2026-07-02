using OrderSphere.BuildingBlocks.Primitives;

namespace OrderSphere.UserProfile.Domain.Errors;

public static class UserProfileErrors
{
    public static readonly Error ProfileNotFound =
        new("UserProfile.NotFound", "Customer profile not found.", ErrorType.NotFound);

    public static readonly Error AddressNotFound =
        new("UserProfile.Address.NotFound", "Saved address not found.", ErrorType.NotFound);

    public static readonly Error AddressLimitExceeded =
        new("UserProfile.Address.LimitExceeded", "Maximum number of saved addresses reached.", ErrorType.Conflict);

    public static readonly Error OnboardingIncomplete =
        new("UserProfile.OnboardingIncomplete", "A display name is required to complete onboarding.", ErrorType.Conflict);

    public static readonly Error TenantNotFound =
        new("UserProfile.Tenant.NotFound", "Tenant not found.", ErrorType.NotFound);

    public static readonly Error TenantSlugTaken =
        new("UserProfile.Tenant.SlugTaken", "A tenant with this slug already exists.", ErrorType.Conflict);
}
