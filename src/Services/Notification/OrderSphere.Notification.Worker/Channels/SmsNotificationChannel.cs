using OrderSphere.BuildingBlocks.Contracts.Events;

namespace OrderSphere.Notification.Worker.Channels;

/// <summary>
/// SMS channel stub. No provider is wired in this release; all sends are logged.
/// Replace the log call with an ACS SMS / Twilio SDK call when a key is available.
/// </summary>
public sealed class SmsNotificationChannel(ILogger<SmsNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannelType ChannelType => NotificationChannelType.Sms;

    public Task SendOrderConfirmationAsync(OrderPlacedIntegrationEvent evt, CancellationToken ct)
    {
        logger.SmsNotificationStubbed(evt.OrderId, evt.CustomerEmail);
        return Task.CompletedTask;
    }
}
