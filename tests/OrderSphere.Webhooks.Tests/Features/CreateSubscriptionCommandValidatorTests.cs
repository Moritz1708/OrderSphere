using OrderSphere.Webhooks.Application.Features.Subscriptions.CreateSubscription;

namespace OrderSphere.Webhooks.Tests.Features;

public sealed class CreateSubscriptionCommandValidatorTests
{
    private readonly CreateSubscriptionCommandValidator _validator = new();

    private static CreateSubscriptionCommand ValidCommand(
        string url = "https://example.com/hook",
        WebhookEventType[]? events = null) =>
        new(Guid.NewGuid(), url, null, events ?? [WebhookEventType.OrderPlaced]);


    [Fact]
    public async Task Validate_EmptyUrl_Fails()
    {
        var result = await _validator.ValidateAsync(ValidCommand(url: ""));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_NonHttpsUrl_Fails()
    {
        var result = await _validator.ValidateAsync(ValidCommand(url: "http://example.com/hook"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_RelativeUrl_Fails()
    {
        var result = await _validator.ValidateAsync(ValidCommand(url: "/relative/path"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_HttpsUrl_Passes()
    {
        var result = await _validator.ValidateAsync(ValidCommand(url: "https://example.com/hook"));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://ordersphere-catalog/api/v1/products")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://user:pw@example.com/hook")]
    public async Task Validate_InternalTarget_Fails(string url)
    {
        var result = await _validator.ValidateAsync(ValidCommand(url: url));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_OverlongUrl_Fails()
    {
        var result = await _validator.ValidateAsync(ValidCommand(url: "https://example.com/" + new string('a', 2048)));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_UndefinedEventType_Fails()
    {
        var result = await _validator.ValidateAsync(ValidCommand(events: [(WebhookEventType)999]));
        result.IsValid.Should().BeFalse();
    }


    [Fact]
    public async Task Validate_EmptyEvents_Fails()
    {
        var result = await _validator.ValidateAsync(ValidCommand(events: []));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_AtLeastOneEvent_Passes()
    {
        var result = await _validator.ValidateAsync(ValidCommand(events: [WebhookEventType.OrderPlaced]));
        result.IsValid.Should().BeTrue();
    }
}
