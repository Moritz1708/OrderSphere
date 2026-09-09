using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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

    /// <summary>
    /// This is the canonical encoding of a deliberate decision, not just an absence check:
    /// anonymous traffic and background work open no tenant scope, so <c>tenant_id</c> is absent
    /// rather than stamped with <c>TenantId.Default</c>. An all-zero GUID on every anonymous
    /// request would be indistinguishable from a genuine single-organisation tenant.
    /// </summary>
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

    /// <summary>
    /// The record an operator reaches for first — an unhandled 500 — is the one the ambient scopes
    /// cannot cover. <c>UseExceptionHandler()</c> has to sit outside <c>UseOrderSphereRequestLogging()</c>
    /// to catch anything at all (every host registers them in that order), so by the time it logs,
    /// the exception has already unwound past the enrichment middleware and disposed both scopes.
    /// This pins the <c>HttpContext.Items</c> fallback that closes the gap; it fails against a
    /// build where the enricher only reads the <c>AsyncLocal</c> slots.
    /// </summary>
    [Fact]
    public async Task Unhandled_exception_record_carries_the_correlation_id_after_the_scope_unwound()
    {
        const string correlationId = "corr-survives-unwind";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        builder.Services.AddOrderSphereRequestLogging();
        builder.Services.AddProblemDetails();
        builder.Logging.AddFakeLogging();

        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.UseOrderSphereRequestLogging();
        app.MapGet("/boom", void () => throw new InvalidOperationException("deliberate"));

        await app.StartAsync();

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Request-Id", correlationId);
        await client.GetAsync("/boom");

        var errorRecord = app.Services.GetFakeLogCollector().GetSnapshot()
            .Should().ContainSingle(r => r.Level == LogLevel.Error).Subject;

        TagsOf(errorRecord).Should().Contain("correlation_id", correlationId);
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
