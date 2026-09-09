using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace OrderSphere.Notification.Tests.Logging;

/// <summary>
/// Regression guard for the PII leak this overhaul closed: the Notification worker used to log
/// raw customer email addresses at Information. The classified [LoggerMessage] parameters must
/// never produce the address in the record — neither in a structured tag nor in the rendered
/// message.
/// </summary>
public sealed class LogRedactionTests
{
    private const string CustomerEmail = "anna.beispiel@example.com";

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Logging.AddFakeLogging();
        return builder.Build();
    }

    [Fact]
    public void Classified_email_never_reaches_the_sink_in_plaintext()
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LogRedactionTests>>();

        Worker.Log.OrderConfirmationEmailSent(logger, Guid.NewGuid(), CustomerEmail);

        var record = host.Services.GetFakeLogCollector().GetSnapshot().Single();

        record.Message.Should().NotContain(CustomerEmail);
        record.Message.Should().NotContain("example.com");
        record.StructuredState!.Select(kvp => kvp.Value)
            .Should().NotContain(v => v != null && v.Contains("example.com"));
    }

    [Fact]
    public void Redaction_is_stable_so_records_for_one_customer_still_group()
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LogRedactionTests>>();

        Worker.Log.OrderConfirmationEmailSent(logger, Guid.NewGuid(), CustomerEmail);
        Worker.Log.OrderConfirmationEmailSent(logger, Guid.NewGuid(), CustomerEmail);
        Worker.Log.OrderConfirmationEmailSent(logger, Guid.NewGuid(), "other@example.com");

        var recipients = host.Services.GetFakeLogCollector().GetSnapshot()
            .Select(r => r.StructuredState!.Single(kvp => kvp.Key == "recipient").Value)
            .ToList();

        recipients[0].Should().Be(recipients[1], "the same address must hash to the same value");
        recipients[2].Should().NotBe(recipients[0], "different addresses must stay distinguishable");
    }

    [Fact]
    public void Unclassified_arguments_are_untouched()
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LogRedactionTests>>();
        var orderId = Guid.NewGuid();

        Worker.Log.OrderConfirmationEmailSent(logger, orderId, CustomerEmail);

        host.Services.GetFakeLogCollector().GetSnapshot().Single()
            .StructuredState!.Should().Contain(kvp => kvp.Key == "orderId" && kvp.Value == orderId.ToString());
    }
}
