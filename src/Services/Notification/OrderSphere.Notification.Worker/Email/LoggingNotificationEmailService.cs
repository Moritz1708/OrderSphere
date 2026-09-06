using OrderSphere.BuildingBlocks.Contracts.Events;

namespace OrderSphere.Notification.Worker.Email;

internal sealed class LoggingNotificationEmailService(ILogger<LoggingNotificationEmailService> logger)
    : INotificationEmailService
{
    public Task SendOrderConfirmationAsync(OrderPlacedIntegrationEvent evt, CancellationToken ct = default)
    {
        logger.OrderConfirmationEmailSuppressed(evt.OrderId, evt.TrackingNumber, evt.CustomerEmail);
        return Task.CompletedTask;
    }

    public Task SendInvoiceReadyAsync(InvoiceGeneratedIntegrationEvent evt, CancellationToken ct = default)
    {
        logger.InvoiceReadyEmailSuppressed(evt.InvoiceNumber, evt.OrderId, evt.CustomerEmail);
        return Task.CompletedTask;
    }
}
