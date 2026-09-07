using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using OrderSphere.Advisory.Api.Agent;
using OrderSphere.Advisory.Api.Configuration;
using OrderSphere.Advisory.Api.Endpoints;
using OrderSphere.Advisory.Api.Voice;
using OrderSphere.Advisory.Api.Workers;
using OrderSphere.Advisory.Infrastructure;
using OrderSphere.Advisory.Infrastructure.Persistence;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using OrderSphere.BuildingBlocks.EventBus.Inbox;

var builder = WebApplication.CreateBuilder(args);

// Azure App Configuration — centralised, versioned prompt storage.
// Enabled when AppConfiguration:Endpoint is set (e.g. via user-secrets locally, Key Vault in Azure).
// Without it the service falls back to the built-in DefaultSystemInstructions constant.
// Label selects the prompt version; defaults to no label (production baseline).
//   dotnet user-secrets set "AppConfiguration:Endpoint" "https://<name>.azconfig.io"
//   dotnet user-secrets set "AppConfiguration:Label"    "v2"          # optional
var appConfigEndpoint = builder.Configuration["AppConfiguration:Endpoint"];
if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
{
    var appConfigLabel = builder.Configuration["AppConfiguration:Label"] ?? "\0";
    builder.Configuration.AddAzureAppConfiguration(options =>
        options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
               .Select("Advisory:*", appConfigLabel));
}

// Aspire defaults (OpenTelemetry, health checks, service discovery, resilience).
builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere Advisory API");

// Redis distributed lock — guards ConversationCleanupService against double-execution at scale.
// Must precede AddAdvisoryInfrastructure which registers the cleanup hosted service.
var redisMultiplexer = await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();

// HybridCache (L1=in-process, L2=Redis) — backing store for the semantic LLM cache.
builder.Services.AddHybridCache();

// EF Core persistence for conversation history (advisory-db).
builder.AddAdvisoryInfrastructure();

// D1 — GDPR right-to-erasure: consumes UserProfile's fan-out queue, hard-deletes conversations.
builder.AddAzureServiceBusClient("azure-service-bus");
builder.Services.AddScoped<IInboxStore, EfInboxStore<AdvisoryDbContext>>();
builder.Services.AddHostedService<CustomerErasureProcessor>();

// erasure-advisory is a GDPR erasure queue: a dead-lettered message here is an unfulfilled
// deletion request, so it needs the same depth gauge and replay surface as its three sibling
// erasure queues in Ordering, Payment and Invoicing.
builder.Services.AddDlqAdmin("erasure-advisory");

// Retention cleanup: processed inbox rows and audit log entries past their retention window.
builder.Services.AddScheduledJob<InboxCleanupJob<AdvisoryDbContext>>();
builder.Services.AddScheduledJob<AuditLogRetentionJob<AdvisoryDbContext>>();

// Auth0 JWT validation. The end-user token is forwarded by the BFF; the agent
// passes it on to the MCP server. Audience is validated downstream, not here —
// hence the no-audience overload (ValidateAudience = false).
builder.AddOrderSphereJwtAuth();
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));

builder.AddOrderSphereExceptionHandling();
builder.Services.AddAdvisoryApiVersioning();
builder.Services.AddAdvisorRateLimiting(redisMultiplexer);

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by AdvisoryDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<AdvisoryDbContext>>();

// Shared Foundry chat-client pipeline (credential, history reducer, GenAI telemetry).
builder.Services.AddSingleton<IAdvisorChatClientFactory, FoundryChatClientFactory>();

// Per-turn MCP connection carrying the caller's bearer token.
builder.Services.AddSingleton<IAdvisorToolSource, McpAdvisorToolSource>();

// GenAI spans (model calls, latency, token usage) emitted by the chat-client
// pipeline; registering the source routes them to the Aspire dashboard via OTLP.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(FoundryChatClientFactory.TelemetrySourceName));

// Scoped: the chat service depends on the per-request DbContext.
builder.Services.AddScoped<AdvisorChatService>();

// Singleton: SpeechService holds a cached AAD token; disabled gracefully without Speech:Region.
builder.Services.AddSingleton<SpeechService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AdvisoryDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseOrderSphereSwagger(docTitle: "OrderSphere Advisory API");
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOrderSphereRequestLogging();

app.MapDefaultEndpoints();
app.MapAdvisorEndpoints();

// Admin audit-log surface — the gateway forwards /api/v1/admin/advisory/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/advisory/audit-log", "AdminPolicy");
app.MapDlqAdminEndpoints("api/v1/admin/advisory/dlq", "AdminPolicy");

app.Run();
