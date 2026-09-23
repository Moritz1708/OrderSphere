using System.Net;
using System.Net.Sockets;
using OrderSphere.Webhooks.Application.Security;

namespace OrderSphere.Webhooks.Worker.Delivery;

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> for webhook deliveries. Resolves the target
/// itself and connects only when every resolved address passes
/// <see cref="WebhookTargetPolicy.IsBlockedAddress"/>. Checking at connect time — not only when the
/// subscription is saved — covers host names that resolve (or re-resolve) to internal addresses,
/// and hosts rewritten by service discovery before the request reaches this handler.
/// </summary>
internal static class WebhookTargetConnector
{
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        // Reject the host if any address is blocked, rather than connecting to the allowed
        // subset: mixed answers are a known way to slip an internal address past a filter.
        if (addresses.Length == 0 || addresses.Any(WebhookTargetPolicy.IsBlockedAddress))
            throw new HttpRequestException($"Webhook target host '{host}' resolves to a blocked address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
