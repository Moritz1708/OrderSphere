namespace OrderSphere.Web.Models;

/// <summary>
/// Editable address state shared by Checkout, Profile and Onboarding through the
/// AddressForm component. Country keeps the German display strings the backend
/// already stores ("Deutschland", "Österreich", "Schweiz"); switching to ISO
/// codes would be a contract change.
/// </summary>
public sealed class AddressFormModel
{
    public string Label { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = "Deutschland";
    public string Email { get; set; } = string.Empty;

    /// <summary>True when every address field (not label or email) has a value.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(FirstName) &&
        !string.IsNullOrWhiteSpace(LastName) &&
        !string.IsNullOrWhiteSpace(Street) &&
        !string.IsNullOrWhiteSpace(PostalCode) &&
        !string.IsNullOrWhiteSpace(City) &&
        !string.IsNullOrWhiteSpace(Country);

    /// <summary>True when the user has started typing an address at all.</summary>
    public bool IsTouched =>
        !string.IsNullOrWhiteSpace(FirstName) ||
        !string.IsNullOrWhiteSpace(LastName) ||
        !string.IsNullOrWhiteSpace(Street) ||
        !string.IsNullOrWhiteSpace(PostalCode) ||
        !string.IsNullOrWhiteSpace(City);

    public CheckoutAddress ToCheckoutAddress() =>
        new(FirstName.Trim(), LastName.Trim(), Street.Trim(), City.Trim(), PostalCode.Trim(), Country);

    public CreateAddressRequest ToCreateAddressRequest(string? label = null, bool setAsDefault = false) =>
        new(
            string.IsNullOrWhiteSpace(label) ? Label.Trim() : label.Trim(),
            FirstName.Trim(), LastName.Trim(), Street.Trim(), City.Trim(), PostalCode.Trim(), Country,
            setAsDefault);

    public void Clear()
    {
        Label = FirstName = LastName = Street = PostalCode = City = Email = string.Empty;
        Country = "Deutschland";
    }
}
