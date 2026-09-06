using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.Security;
using Xunit;

namespace OrderSphere.EventBus.AzureServiceBus.Tests;

public sealed class MessageProcessingScopeTests
{
    private static ServiceBusReceivedMessage Message(
        string messageId = "m-1",
        string? subject = "OrderPlacedIntegrationEvent",
        IDictionary<string, object>? properties = null) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            messageId: messageId,
            subject: subject,
            properties: properties);

    [Fact]
    public void Begin_opens_the_correlation_slot_from_the_message()
    {
        var message = Message(properties: new Dictionary<string, object> { ["x-request-id"] = "corr-42" });

        using (MessageProcessingScope.Begin(NullLogger.Instance, message, "orders"))
        {
            AmbientCorrelationContext.Ambient.Should().Be("corr-42");
        }

        AmbientCorrelationContext.Ambient.Should().BeNull();
    }

    [Fact]
    public void Begin_falls_back_to_the_trace_id_when_no_correlation_property_is_present()
    {
        // Messages published through the outbox carry only traceparent; the trace id is the
        // correlation id by construction (the gateway seeds X-Request-Id from it).
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var spanId = ActivitySpanId.CreateRandom().ToString();
        var message = Message(properties: new Dictionary<string, object>
        {
            ["traceparent"] = $"00-{traceId}-{spanId}-01",
        });

        using (MessageProcessingScope.Begin(NullLogger.Instance, message, "orders"))
        {
            AmbientCorrelationContext.Ambient.Should().Be(traceId);
        }
    }

    [Fact]
    public void Begin_falls_back_to_the_message_id_when_nothing_is_carried()
    {
        using (MessageProcessingScope.Begin(NullLogger.Instance, Message(messageId: "m-99"), "orders"))
        {
            AmbientCorrelationContext.Ambient.Should().Be("m-99");
        }
    }

    [Fact]
    public void SetTenant_opens_the_tenant_slot_and_disposal_restores_it()
    {
        var tenantId = Guid.NewGuid();

        using (var scope = MessageProcessingScope.Begin(NullLogger.Instance, Message(), "orders"))
        {
            AmbientTenantContext.Ambient.Should().BeNull();
            scope.SetTenant(tenantId);
            AmbientTenantContext.Ambient.Should().Be(tenantId);
        }

        AmbientTenantContext.Ambient.Should().BeNull();
    }

    [Fact]
    public void EventType_comes_from_the_subject_and_falls_back_to_the_application_property()
    {
        using (var fromSubject = MessageProcessingScope.Begin(NullLogger.Instance, Message(), "orders"))
        {
            fromSubject.EventType.Should().Be("OrderPlacedIntegrationEvent");
        }

        var message = Message(subject: null, properties: new Dictionary<string, object>
        {
            ["EventType"] = "PaymentSucceededIntegrationEvent",
        });

        using (var fromProperty = MessageProcessingScope.Begin(NullLogger.Instance, message, "payments"))
        {
            fromProperty.EventType.Should().Be("PaymentSucceededIntegrationEvent");
        }
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var scope = MessageProcessingScope.Begin(NullLogger.Instance, Message(), "orders");
        scope.SetTenant(Guid.NewGuid());

        scope.Dispose();
        var act = scope.Dispose;

        act.Should().NotThrow();
        AmbientTenantContext.Ambient.Should().BeNull();
    }
}
