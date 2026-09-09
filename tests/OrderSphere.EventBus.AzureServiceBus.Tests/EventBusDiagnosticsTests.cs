using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.Security;
using Xunit;

namespace OrderSphere.EventBus.AzureServiceBus.Tests;

/// <summary>
/// Covers the outbox correlation boundary. There was no coverage here before, which is how the
/// assumption that <c>correlation_id == trace_id</c> survived as a code comment while two
/// production paths quietly broke it: the API Gateway honours a client-supplied
/// <c>X-Request-Id</c>, and a consumer falls back to the Service Bus message id.
/// </summary>
public sealed class EventBusDiagnosticsTests
{
    private const string SampleTraceParent =
        "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private const string SampleTraceId = "0af7651916cd43dd8448eb211c80319c";

    [Fact]
    public void Persisted_correlation_id_wins_over_the_trace_id()
    {
        // The case the old derivation got wrong: a client chose the id, so it is not the trace id.
        using (EventBusDiagnostics.RestorePublishParent(SampleTraceParent, "client-supplied-id"))
        {
            AmbientCorrelationContext.Ambient.Should().Be("client-supplied-id");
        }
    }

    [Fact]
    public void Falls_back_to_the_trace_id_for_rows_written_before_the_column_existed()
    {
        // Backwards compatibility only. Those rows really were correlated by the trace id,
        // because the gateway seeded X-Request-Id from it.
        using (EventBusDiagnostics.RestorePublishParent(SampleTraceParent, correlationId: null))
        {
            AmbientCorrelationContext.Ambient.Should().Be(SampleTraceId);
        }
    }

    [Fact]
    public void Opens_a_scope_even_without_a_usable_trace_context()
    {
        // Previously this opened no scope at all, so Inject() omitted x-request-id, the consumer
        // fell through to the message id, and that value was persisted on the next outbox hop —
        // severing the chain permanently.
        using (EventBusDiagnostics.RestorePublishParent(traceParent: null, "corr-1"))
        {
            AmbientCorrelationContext.Ambient.Should().Be("corr-1");
        }
    }

    [Fact]
    public void Injects_the_correlation_id_onto_the_message_without_a_trace_context()
    {
        var message = new ServiceBusMessage();

        using (EventBusDiagnostics.RestorePublishParent(traceParent: null, "corr-1"))
        {
            EventBusDiagnostics.Inject(message);
        }

        message.ApplicationProperties.Should().ContainKey("x-request-id")
            .WhoseValue.Should().Be("corr-1");
    }

    [Fact]
    public void Opens_no_scope_when_there_is_neither_trace_nor_correlation()
    {
        using (EventBusDiagnostics.RestorePublishParent(traceParent: null, correlationId: null))
        {
            AmbientCorrelationContext.Ambient.Should().BeNull();
        }
    }

    [Fact]
    public void Disposal_restores_the_previous_ambient_value()
    {
        using (AmbientCorrelationContext.BeginScope("outer"))
        {
            using (EventBusDiagnostics.RestorePublishParent(SampleTraceParent, "inner"))
            {
                AmbientCorrelationContext.Ambient.Should().Be("inner");
            }

            AmbientCorrelationContext.Ambient.Should().Be("outer");
        }
    }

    [Fact]
    public void Round_trips_a_client_supplied_id_from_publish_to_consume()
    {
        // The end-to-end property the CorrelationId column exists to guarantee: an id chosen at
        // the edge survives the outbox boundary and is what the consumer reads back.
        var published = new ServiceBusMessage();

        using (EventBusDiagnostics.RestorePublishParent(SampleTraceParent, "client-supplied-id"))
        {
            EventBusDiagnostics.Inject(published);
        }

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            messageId: Guid.NewGuid().ToString(),
            properties: published.ApplicationProperties);

        EventBusDiagnostics.ReadCorrelationId(received).Should().Be("client-supplied-id");
    }
}
