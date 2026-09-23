using OrderSphere.Webhooks.Application.Features.Subscriptions.UpdateSubscription;

namespace OrderSphere.Webhooks.Tests.Features;

public sealed class UpdateSubscriptionCommandValidatorTests
{
    private readonly UpdateSubscriptionCommandValidator _validator = new();

    private static UpdateSubscriptionCommand Command(
        string url = "https://example.com/hook",
        string? secret = null,
        WebhookEventType[]? events = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), url, secret, events ?? [WebhookEventType.OrderPlaced]);

    [Fact]
    public async Task Validate_PublicHttpsUrl_Passes()
    {
        var result = await _validator.ValidateAsync(Command());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://ordersphere-payment/")]
    [InlineData("https://169.254.169.254/")]
    public async Task Validate_NonHttpsOrInternalTarget_Fails(string url)
    {
        var result = await _validator.ValidateAsync(Command(url: url));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_OverlongSecret_Fails()
    {
        var result = await _validator.ValidateAsync(Command(secret: new string('s', 257)));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyEvents_Fails()
    {
        var result = await _validator.ValidateAsync(Command(events: []));
        result.IsValid.Should().BeFalse();
    }
}
