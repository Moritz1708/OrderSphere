using OrderSphere.BuildingBlocks.Compliance;

namespace OrderSphere.Notification.Worker;

/// <summary>
/// Source-generated log methods for the Notification worker.
///
/// This service is the only one that handles customer email addresses in its hot path, so every
/// log statement that touches one lives here: redaction applies to classified
/// <c>[LoggerMessage]</c> parameters only, never to a plain <c>logger.LogInformation(...)</c>
/// call. Marking the parameter <see cref="DirectPiiAttribute"/> makes the pipeline replace the
/// address with a keyed hash before it reaches any sink, so records stay groupable per customer
/// without carrying the address.
///
/// EventId range 8000-8999 (see docs/logging.md).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 8001,
        Level = LogLevel.Information,
        Message = "Invoice-ready email sent for invoice {invoiceNumber}.")]
    public static partial void InvoiceReadyEmailSent(
        this ILogger logger,
        string invoiceNumber,
        [DirectPii] string recipient);

    [LoggerMessage(
        EventId = 8002,
        Level = LogLevel.Information,
        Message = "Confirmation email sent for order {orderId}.")]
    public static partial void OrderConfirmationEmailSent(
        this ILogger logger,
        Guid orderId,
        [DirectPii] string recipient);

    [LoggerMessage(
        EventId = 8003,
        Level = LogLevel.Warning,
        Message = "Failed to send invoice-ready email for invoice {invoiceNumber}.")]
    public static partial void InvoiceReadyEmailFailed(
        this ILogger logger,
        Exception exception,
        string invoiceNumber);

    [LoggerMessage(
        EventId = 8004,
        Level = LogLevel.Warning,
        Message = "Failed to send confirmation email for order {orderId}.")]
    public static partial void OrderConfirmationEmailFailed(
        this ILogger logger,
        Exception exception,
        Guid orderId);

    [LoggerMessage(
        EventId = 8005,
        Level = LogLevel.Information,
        Message = "[DEV] Order confirmation email suppressed for order {orderId}, tracking {trackingNumber}.")]
    public static partial void OrderConfirmationEmailSuppressed(
        this ILogger logger,
        Guid orderId,
        string trackingNumber,
        [DirectPii] string recipient);

    [LoggerMessage(
        EventId = 8006,
        Level = LogLevel.Information,
        Message = "[DEV] Invoice-ready email suppressed for invoice {invoiceNumber}, order {orderId}.")]
    public static partial void InvoiceReadyEmailSuppressed(
        this ILogger logger,
        string invoiceNumber,
        Guid orderId,
        [DirectPii] string recipient);

    [LoggerMessage(
        EventId = 8007,
        Level = LogLevel.Information,
        Message = "[SMS] Order confirmation for order {orderId} would be sent. (No SMS provider configured.)")]
    public static partial void SmsNotificationStubbed(
        this ILogger logger,
        Guid orderId,
        [DirectPii] string recipient);

    [LoggerMessage(
        EventId = 8008,
        Level = LogLevel.Information,
        Message = "[Push] Order confirmation for order {orderId} would be pushed. (No push provider configured.)")]
    public static partial void PushNotificationStubbed(
        this ILogger logger,
        Guid orderId,
        [DirectPii] string recipient);

    [LoggerMessage(
        EventId = 8009,
        Level = LogLevel.Warning,
        Message = "UserProfile returned {status} for a customer lookup. Using default preferences.")]
    public static partial void NotificationPreferencesUnavailable(
        this ILogger logger,
        int status,
        [DirectPii] string customerEmail);

    [LoggerMessage(
        EventId = 8010,
        Level = LogLevel.Warning,
        Message = "Failed to fetch notification preferences. Using defaults.")]
    public static partial void NotificationPreferencesFetchFailed(
        this ILogger logger,
        Exception exception,
        [DirectPii] string customerEmail);

    [LoggerMessage(
        EventId = 8011,
        Level = LogLevel.Debug,
        Message = "UserProfile URL not configured; using default notification preferences.")]
    public static partial void NotificationPreferencesDefaulted(
        this ILogger logger,
        [DirectPii] string customerEmail);
}
