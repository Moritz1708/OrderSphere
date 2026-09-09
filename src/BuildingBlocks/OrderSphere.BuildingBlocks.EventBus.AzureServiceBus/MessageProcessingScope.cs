using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;

/// <summary>
/// Opens the full ambient context for one inbound Service Bus message: the consumer span, the
/// tenant slot, the log-correlation slot and a log scope carrying the message identifiers.
/// <para>
/// This is the worker-side counterpart to the request middleware in ServiceDefaults. Because it
/// sets the same <see cref="AmbientTenantContext"/> and <see cref="AmbientCorrelationContext"/>
/// slots, the shared log enricher produces the same field set on worker records as on API
/// records, without any worker knowing about logging infrastructure.
/// </para>
/// <para>
/// Tenant is not known until the body is deserialized, so the scope is opened in two steps:
/// construct it when the message arrives, then call <see cref="SetTenant"/> once the event is
/// deserialized. Disposal restores every slot in reverse order.
/// </para>
/// </summary>
public sealed class MessageProcessingScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly IDisposable _correlationScope;
    private readonly IDisposable? _logScope;
    private IDisposable? _tenantScope;
    private bool _disposed;

    private MessageProcessingScope(
        Activity? activity,
        IDisposable correlationScope,
        IDisposable? logScope,
        string messageId,
        string eventType)
    {
        _activity = activity;
        _correlationScope = correlationScope;
        _logScope = logScope;
        MessageId = messageId;
        EventType = eventType;
    }

    public string MessageId { get; }

    public string EventType { get; }

    /// <summary>
    /// Begins processing scope for <paramref name="message"/> received from <paramref name="queueName"/>.
    /// </summary>
    public static MessageProcessingScope Begin(
        ILogger logger,
        ServiceBusReceivedMessage message,
        string queueName)
    {
        var activity = EventBusDiagnostics.StartProcess(message, queueName);
        var correlationScope = AmbientCorrelationContext.BeginScope(
            EventBusDiagnostics.ReadCorrelationId(message));

        var eventType = message.Subject
            ?? (message.ApplicationProperties.TryGetValue("EventType", out var raw) ? raw as string : null)
            ?? "unknown";

        var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["message_id"] = message.MessageId,
            ["event_type"] = eventType,
            ["queue"] = queueName,
        });

        return new MessageProcessingScope(activity, correlationScope, logScope, message.MessageId, eventType);
    }

    /// <summary>
    /// Opens the ambient tenant slot once the event body has been deserialized. Persistence code
    /// reached from here inherits the tenant through the global query filters (ADR 0012).
    /// </summary>
    public void SetTenant(Guid tenantId)
    {
        _tenantScope?.Dispose();
        _tenantScope = AmbientTenantContext.BeginScope(tenantId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tenantScope?.Dispose();
        _logScope?.Dispose();
        _correlationScope.Dispose();
        _activity?.Dispose();
    }
}
