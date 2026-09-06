using OrderSphere.BuildingBlocks.Contracts.Events;

namespace OrderSphere.Notification.Worker.Channels;

/// <summary>
/// Push-notification channel stub. No provider is wired in this release; all sends are logged.
/// Replace the log call with a Web Push / Firebase Cloud Messaging call when a key is available.
/// </summary>
public sealed class PushNotificationChannel(ILogger<PushNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannelType ChannelType => NotificationChannelType.Push;

    public Task SendOrderConfirmationAsync(OrderPlacedIntegrationEvent evt, CancellationToken ct)
    {
        logger.PushNotificationStubbed(evt.OrderId, evt.CustomerEmail);
        return Task.CompletedTask;
    }
}
