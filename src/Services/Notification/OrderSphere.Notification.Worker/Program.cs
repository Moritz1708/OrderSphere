using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Notification.Worker.Channels;
using OrderSphere.Notification.Worker.Clients;
using OrderSphere.Notification.Worker.Email;
using OrderSphere.Notification.Worker.Persistence;
using OrderSphere.Notification.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// PostgreSQL — inbox for idempotent message processing
builder.AddNpgsqlDbContext<NotificationDbContext>("notification-db");
builder.Services.AddScoped<IInboxStore, EfInboxStore<NotificationDbContext>>();

// Retention cleanup: processed inbox rows past their retention window (Retention:InboxDays, default 30).
builder.Services.AddScheduledJob<InboxCleanupJob<NotificationDbContext>>();

// Azure Service Bus
builder.AddAzureServiceBusClient("azure-service-bus");

// Email service — in development without mail config, log-only mock is used.
var mailConfig = builder.Configuration.GetSection("MailServiceConfiguration");
var connectionString = mailConfig["ConnectionString"];
var senderAddress = mailConfig["SenderAddress"];

if (!string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(senderAddress))
{
    builder.Services.AddSingleton<INotificationEmailService>(sp =>
        new NotificationEmailService(
            connectionString,
            senderAddress,
            sp.GetRequiredService<ILogger<NotificationEmailService>>()));
}
else
{
    builder.Services.AddSingleton<INotificationEmailService, LoggingNotificationEmailService>();
}

// Notification channels — strategy implementations registered as INotificationChannel.
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, SmsNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, PushNotificationChannel>();

// UserProfile client — fetches per-user channel opt-in state before dispatching.
// D4 — ClientCredentialsTokenHandler attaches a Bearer token acquired with this worker's
// own Oidc:ClientId/ClientSecret; UserProfile's internal endpoint now requires it.
var userProfileUrl = builder.Configuration["Services:UserProfile:BaseUrl"];
if (!string.IsNullOrWhiteSpace(userProfileUrl))
{
    builder.Services.AddHttpClient<IUserProfileClient, HttpUserProfileClient>(client =>
    {
        client.BaseAddress = new Uri(userProfileUrl);
    }).AddClientCredentialsHandler();
}
else
{
    builder.Services.AddSingleton<IUserProfileClient, FallbackUserProfileClient>();
}

builder.Services.AddHostedService<NotificationProcessor>();
builder.Services.AddHostedService<InvoiceGeneratedProcessor>();

// DLQ admin surface: admin-protected dead-letter reader/replay for this worker's queues, plus the
// ordersphere.dlq.depth gauge. JWT auth mirrors the API services (Oidc config is already injected).
builder.AddOrderSphereJwtAuth("notification-worker");
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));
builder.Services.AddDlqAdmin("notification-orders", "invoice-ready");

var app = builder.Build();

// Apply EF migrations on startup (dev convenience)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    db.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

// Admin DLQ surface — the gateway forwards /api/v1/admin/notification/dlq/** here.
app.MapDlqAdminEndpoints("api/v1/admin/notification/dlq", "AdminPolicy");

// Liveness/readiness endpoints (/health, /alive, /version) for container probes.
app.MapDefaultEndpoints();

app.Run();
