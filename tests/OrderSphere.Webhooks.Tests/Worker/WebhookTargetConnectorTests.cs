using OrderSphere.Webhooks.Worker.Delivery;

namespace OrderSphere.Webhooks.Tests.Worker;

/// <summary>
/// The connect-time check is the authoritative SSRF guard: it sees the address actually being
/// dialled, including names that pass the save-time URL check but resolve internally.
/// </summary>
public sealed class WebhookTargetConnectorTests
{
    private static HttpClient Client() => new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectCallback = WebhookTargetConnector.ConnectAsync,
    });

    [Theory]
    [InlineData("https://127.0.0.1:9/hook")]
    [InlineData("https://localhost:9/hook")]
    [InlineData("https://[::1]:9/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public async Task Connect_ToBlockedAddress_FailsBeforeDialling(string url)
    {
        using var client = Client();

        var act = () => client.PostAsync(url, new StringContent("{}"));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("blocked address");
    }
}
