namespace OrderSphere.UserProfile.Application.Models;

public sealed record ProfileDto(
    Guid Id,
    string Subject,
    string DisplayName,
    string Email,
    bool DarkModeEnabled,
    bool IsOnboardingComplete,
    IReadOnlyList<AddressDto> Addresses);

public sealed record AddressDto(
    Guid Id,
    string Label,
    string FirstName,
    string LastName,
    string Street,
    string City,
    string PostalCode,
    string Country,
    bool IsDefault);

public sealed record UpdateProfileRequest(string DisplayName, string Email);

public sealed record UpdatePreferencesRequest(bool DarkModeEnabled);

public sealed record NotificationPreferencesDto(
    bool EmailEnabled,
    bool SmsEnabled,
    bool PushEnabled,
    DateTime? ConsentedAt);

public sealed record UpdateNotificationPreferencesRequest(
    bool EmailEnabled,
    bool SmsEnabled,
    bool PushEnabled);

public sealed record CreateAddressRequest(
    string Label,
    string FirstName,
    string LastName,
    string Street,
    string City,
    string PostalCode,
    string Country,
    bool SetAsDefault = false);

public sealed record UpdateAddressRequest(
    string Label,
    string FirstName,
    string LastName,
    string Street,
    string City,
    string PostalCode,
    string Country);

public sealed record AdminUserSummaryDto(
    Guid Id,
    string Subject,
    string DisplayName,
    string Email,
    bool DarkModeEnabled,
    int AddressCount);

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    DateTime CreatedAt);

public sealed record CreateTenantRequest(string Name, string Slug);
