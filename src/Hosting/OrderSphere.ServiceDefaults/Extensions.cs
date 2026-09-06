using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using OrderSphere.BuildingBlocks.Compliance;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string VersionEndpointPath = "/version";

    // Same version source as the /version endpoint; stamped onto every telemetry signal as service.version.
    private static readonly string ServiceVersion =
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? "unknown";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();

            // Carry the log correlation id (X-Request-Id) across service hops.
            http.AddHttpMessageHandler<CorrelationPropagationHandler>();
        });

        builder.Services.AddTransient<CorrelationPropagationHandler>();

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        // Structured HTTP request logging (method, path, status, duration, user_id, client_ip).
        // Body content is never logged to avoid PII exposure.
        builder.Services.AddOrderSphereRequestLogging();

        // Security audit logger available to all services (APIs, workers, gateways).
        builder.Services.AddSecurityAuditLogger();

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.ConfigureLogEnrichment();
        builder.ConfigureLogRedaction();

        // EF Core logs every SQL command at Information by default. Default it to Warning so the
        // database story comes from traces (DB spans), not log spam — raise the
        // "Microsoft.EntityFrameworkCore.Database.Command" category where raw SQL is needed.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

        builder.Services.AddOpenTelemetry()
            // Resource identity: service.name, service.version and deployment.environment make every
            // signal filterable by service and environment in the Aspire dashboard / App Insights.
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: builder.Environment.ApplicationName,
                    serviceVersion: ServiceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)
                ]))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Custom OrderSphere business metrics — single meter name across all services.
                    .AddMeter("OrderSphere");
            })
            .WithTracing(tracing =>
            {
                // Parent-based ratio sampler; ratio from "OpenTelemetry:TracesSampleRatio"
                // (default 1.0 = sample everything). Lower it in production to control cost.
                // Azure Monitor applies its own sampler via APPLICATIONINSIGHTS_SAMPLING_PERCENTAGE.
                //
                // InvariantCulture is required: configuration values are culture-invariant, but a
                // plain double.TryParse uses the host's current culture, where "0.1" on a de-DE
                // machine parses as 1 and "1.0" as 10 - both outside the sampler's [0,1] range.
                // The value is clamped as well so a typo degrades sampling instead of crashing
                // the host at startup.
                var sampleRatio = double.TryParse(
                    builder.Configuration["OpenTelemetry:TracesSampleRatio"],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var ratio) ? Math.Clamp(ratio, 0d, 1d) : 1.0;

                tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
                    .AddSource(builder.Environment.ApplicationName)
                    // Event-bus publish/consume spans (string literal avoids coupling
                    // ServiceDefaults to the EF-bound EventBus.AzureServiceBus project).
                    .AddSource("OrderSphere.EventBus")
                    // Per-request (MediatR/CQRS handler) spans from the LoggingBehavior.
                    .AddSource("OrderSphere.Application")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                            && !context.Request.Path.StartsWithSegments(VersionEndpointPath)
                    )
                    // Client spans for internal gRPC calls (e.g. Basket→Catalog stock checks).
                    .AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Replaces the default ILoggerFactory with ExtendedLoggerFactory and registers the
    /// OrderSphere enrichers. Enrichment tags are written into the log-record state, which the
    /// OpenTelemetry logger provider reads as attributes — so tenant_id / correlation_id /
    /// user_id reach the Aspire dashboard and Application Insights without further wiring.
    /// </summary>
    private static TBuilder ConfigureLogEnrichment<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.EnableEnrichment();

        // OrderSphereLogEnricher is a singleton and reads the user from the accessor; workers
        // simply never have an HttpContext and fall through to the ambient slots.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddLogEnricher<OrderSphereLogEnricher>();
        builder.Services.AddStaticLogEnricher<OrderSphereStaticLogEnricher>();

        return builder;
    }

    /// <summary>
    /// Activates redaction for classified [LoggerMessage] parameters. The classifications and
    /// their mapping to the sensitivity tiers in docs/data-classification.md live in
    /// <see cref="OrderSphereDataClassifications"/>.
    /// <para>
    /// Redaction applies only to source-generated log methods whose parameters carry a
    /// classification attribute — never to plain logger.LogX(...) calls.
    /// </para>
    /// </summary>
    private static TBuilder ConfigureLogRedaction<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.EnableRedaction();

        // Key material is per-deployment: two environments produce unrelated hashes, so a value
        // cannot be correlated across them. Absent configuration (local dev, tests) a process-
        // lifetime random key is used — redaction still holds, correlation just ends at restart.
        var hmacKey = builder.Configuration["Logging:Redaction:HmacKey"]
            ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        builder.Services.AddRedaction(redaction =>
        {
            // Direct PII (T1) and pseudonymous identifiers (T2) are hashed rather than erased so
            // that "all records for the same customer" stays answerable without storing the value.
            // Distinct key ids keep the two tiers from being cross-correlated.
            redaction.SetHmacRedactor(
                options =>
                {
                    options.KeyId = 1;
                    options.Key = hmacKey;
                },
                OrderSphereDataClassifications.DirectPii);

            redaction.SetHmacRedactor(
                options =>
                {
                    options.KeyId = 2;
                    options.Key = hmacKey;
                },
                OrderSphereDataClassifications.PseudonymousId);

            // Free text (T4) carries the highest re-identification risk per byte and has no
            // operational value in a log record, so it is dropped outright.
            redaction.SetRedactor<ErasingRedactor>(OrderSphereDataClassifications.FreeText);

            // Anything classified but unmapped is erased rather than leaked.
            redaction.SetFallbackRedactor<ErasingRedactor>();
        });

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Liveness — the app process is running and responsive.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // Readiness dependency checks are registered by the Aspire client integrations:
        // Aspire.Npgsql.EntityFrameworkCore.PostgreSQL (per-service DB), Aspire.StackExchange.Redis,
        // and Aspire.Azure.Messaging.ServiceBus each register their own check under the resource
        // name. There is no shared "ordersphere-db" connection, so no generic DB check is added here.

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(HealthEndpointPath);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        // Expose the compiled product version (from VersionPrefix in Directory.Build.props).
        app.MapGet(VersionEndpointPath, () =>
        {
            var assembly = Assembly.GetEntryAssembly();
            var informational = assembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return Results.Ok(new
            {
                service = assembly?.GetName().Name,
                version = informational ?? assembly?.GetName().Version?.ToString()
            });
        })
        .WithName("Version")
        .ExcludeFromDescription();

        return app;
    }
}
