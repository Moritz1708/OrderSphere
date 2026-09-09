using System.Globalization;
using System.Text;
using Azure;
using Azure.Communication.Email;
using OrderSphere.BuildingBlocks.Contracts.Events;

namespace OrderSphere.Notification.Worker.Email;

public sealed class NotificationEmailService(
    string connectionString,
    string senderAddress,
    ILogger<NotificationEmailService> logger) : INotificationEmailService
{
    public async Task SendInvoiceReadyAsync(InvoiceGeneratedIntegrationEvent evt, CancellationToken ct = default)
    {
        try
        {
            var mailClient = new EmailClient(connectionString);
            var de = CultureInfo.GetCultureInfo("de-DE");
            var subject = $"Ihre Rechnung {evt.InvoiceNumber} – Bestellung {evt.OrderId}";
            var body = evt.PdfUrl.Length > 0
                ? $"Ihre Rechnung steht zum Download bereit: {evt.PdfUrl}"
                : "Ihre Rechnung wurde erstellt. Der Download ist in Kürze verfügbar.";

            var message = new EmailMessage(
                senderAddress: senderAddress,
                recipients: new EmailRecipients([new EmailAddress(evt.CustomerEmail)]),
                content: new EmailContent(subject)
                {
                    PlainText = $"Rechnungsnummer: {evt.InvoiceNumber}\nGesamtbetrag: {evt.Total.ToString("C", de)}\n\n{body}",
                    Html = $"<h3>Rechnung {evt.InvoiceNumber}</h3><p>Gesamtbetrag: <strong>{evt.Total.ToString("C", de)}</strong></p><p>{body}</p>",
                });

            await mailClient.SendAsync(WaitUntil.Completed, message, ct);
            logger.InvoiceReadyEmailSent(evt.InvoiceNumber, evt.CustomerEmail);
        }
        catch (Exception ex)
        {
            logger.InvoiceReadyEmailFailed(ex, evt.InvoiceNumber);
            throw;
        }
    }

    public async Task SendOrderConfirmationAsync(OrderPlacedIntegrationEvent evt, CancellationToken ct = default)
    {
        try
        {
            var mailClient = new EmailClient(connectionString);
            var subject = $"Deine OrderSphere-Bestellung {evt.OrderId} – Tracking {evt.TrackingNumber}";

            var message = new EmailMessage(
                senderAddress: senderAddress,
                recipients: new EmailRecipients([new EmailAddress(evt.CustomerEmail)]),
                content: new EmailContent(subject)
                {
                    PlainText = BuildPlainText(evt),
                    Html = BuildHtml(evt)
                });

            await mailClient.SendAsync(WaitUntil.Completed, message, ct);
            logger.OrderConfirmationEmailSent(evt.OrderId, evt.CustomerEmail);
        }
        catch (Exception ex)
        {
            logger.OrderConfirmationEmailFailed(ex, evt.OrderId);
            throw;
        }
    }

    internal static string BuildPlainText(OrderPlacedIntegrationEvent evt)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder();
        sb.AppendLine("Vielen Dank für deine Bestellung bei OrderSphere!");
        sb.AppendLine();
        sb.AppendLine($"Bestellnummer: {evt.OrderId}");
        sb.AppendLine($"Tracking-Nummer: {evt.TrackingNumber}");
        sb.AppendLine();
        sb.AppendLine("Lieferadresse:");
        sb.AppendLine($"  {evt.ShippingFirstName} {evt.ShippingLastName}");
        sb.AppendLine($"  {evt.ShippingStreet}");
        sb.AppendLine($"  {evt.ShippingPostalCode} {evt.ShippingCity}");
        sb.AppendLine($"  {evt.ShippingCountry}");
        sb.AppendLine();
        sb.AppendLine("Artikel:");
        foreach (var item in evt.Items)
            sb.AppendLine($"  {item.Quantity}x {item.ProductName} – {item.Price.ToString("C", de)}");
        sb.AppendLine();
        sb.AppendLine($"Gesamt: {evt.Total.ToString("C", de)}");
        return sb.ToString();
    }

    internal static string BuildHtml(OrderPlacedIntegrationEvent evt)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var itemRows = new StringBuilder();
        foreach (var item in evt.Items)
        {
            itemRows.Append($@"
                <tr>
                    <td style='padding:8px; border-bottom:1px solid #E5E7EB;'>{item.ProductName}</td>
                    <td style='padding:8px; border-bottom:1px solid #E5E7EB; text-align:center;'>{item.Quantity}</td>
                    <td style='padding:8px; border-bottom:1px solid #E5E7EB; text-align:right;'>{item.Price.ToString("C", de)}</td>
                </tr>");
        }

        return $@"
        <html><body style='font-family:Arial,sans-serif;'>
          <h2 style='color:#6366F1;'>OrderSphere</h2>
          <h3>Vielen Dank für deine Bestellung!</h3>
          <p>Bestellnummer: <strong>{evt.OrderId}</strong></p>
          <p>Tracking-Nummer: <strong>{evt.TrackingNumber}</strong></p>
          <h4>Lieferadresse</h4>
          <p>{evt.ShippingFirstName} {evt.ShippingLastName}<br/>
             {evt.ShippingStreet}<br/>
             {evt.ShippingPostalCode} {evt.ShippingCity}<br/>
             {evt.ShippingCountry}</p>
          <h4>Artikel</h4>
          <table width='100%' style='border-collapse:collapse;'>
            <thead><tr style='background:#F3F4F6;'>
              <th style='padding:8px; text-align:left;'>Produkt</th>
              <th style='padding:8px; text-align:center;'>Menge</th>
              <th style='padding:8px; text-align:right;'>Preis</th>
            </tr></thead>
            <tbody>{itemRows}</tbody>
            <tfoot><tr>
              <td colspan='2' style='padding:12px 8px; text-align:right; font-weight:bold;'>Gesamt:</td>
              <td style='padding:12px 8px; text-align:right; font-weight:bold; color:#6366F1;'>{evt.Total.ToString("C", de)}</td>
            </tr></tfoot>
          </table>
        </body></html>";
    }
}
