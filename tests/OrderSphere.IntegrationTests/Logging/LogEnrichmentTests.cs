using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OrderSphere.BuildingBlocks.Security;
using Xunit;

namespace OrderSphere.IntegrationTests.Logging;

/// <summary>
/// Verifies the enrichment wiring in AddServiceDefaults end-to-end: a log record written
/// anywhere in a service must carry the ambient tenant and correlation id without the call site
/// mentioning them. These run against a plain host (no HTTP), which is exactly the worker case —
/// the point being that worker records get the same fields as API records.
/// </summary>
public sealed class LogEnrichmentTests
{
    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Logging.AddFakeLogging();
        return builder.Build();
    }

    private static IReadOnlyDictionary<string, string?> TagsOf(FakeLogRecord record) =>
        record.StructuredState?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        ?? new Dictionary<string, string?>();

    [Fact]
    public void Record_carries_ambient_tenant_and_correlation()
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LogEnrichmentTests>>();
        var tenantId = Guid.NewGuid();

        using (AmbientTenantContext.BeginScope(tenantId))
        using (AmbientCorrelationContext.BeginScope("abc123"))
        {
            logger.LogInformation("Order {OrderId} processed", 42);
        }

        var tags = TagsOf(host.Services.GetFakeLogCollector().GetSnapshot().Single());

        tags.Should().Contain("tenant_id", tenantId.ToString());
        tags.Should().Contain("correlation_id", "abc123");
        // The call site's own template arguments survive alongside the enrichment.
        tags.Should().Contain("OrderId", "42");
    }

    [Fact]
    public void Record_omits_tenant_and_correlation_when_no_scope_is_open()
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LogEnrichmentTests>>();

        logger.LogInformation("Startup complete");

        var tags = TagsOf(host.Services.GetFakeLogCollector().GetSnapshot().Single());

        tags.Should().NotContainKey("tenant_id");
        tags.Should().NotContainKey("correlation_id");
    }

    [Fact]
    public void Static_enricher_stamps_build_version_on_every_record()
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILogger<LogEnrichmentTests>>();

        logger.LogInformation("Anything");

        TagsOf(host.Services.GetFakeLogCollector().GetSnapshot().Single())
            .Should().ContainKey("build_version")
            .And.ContainKey("service_instance_id");
    }
}
