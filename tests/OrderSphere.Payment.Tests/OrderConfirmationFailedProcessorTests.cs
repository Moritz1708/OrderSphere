using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Domain.Entities;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;
using OrderSphere.Payment.Worker.Workers;
using Xunit;

namespace OrderSphere.Payment.Tests;

public sealed class OrderConfirmationFailedProcessorTests
{
    private const string Method = "creditcard";

    private static PaymentDbContext NewContext() => Helpers.PaymentDbContextFactory.Create();

    private static OrderConfirmationFailedProcessor NewProcessor(bool bypassProviders = false)
        => new(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new PaymentOptions { BypassProviders = bypassProviders }),
            NullLogger<OrderConfirmationFailedProcessor>.Instance);

    private static OrderConfirmationFailedIntegrationEvent NewEvent(Guid orderId, string method = Method) => new()
    {
        OrderId = orderId,
        CorrelationId = Guid.NewGuid(),
        Amount = 49.99m,
        Currency = "EUR",
        Reason = "Reservation confirm failed; refunding payment.",
        CustomerEmail = "customer@example.com",
        PaymentMethod = method
    };

    private static IInboxStore UnprocessedInbox()
    {
        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        return inbox;
    }

    private static async Task<PaymentRecord> SeedCapturedPaymentAsync(PaymentDbContext context, Guid orderId)
    {
        var record = new PaymentRecord(OrderId.From(orderId), 49.99m, "EUR", Method, "customer@example.com", Guid.NewGuid());
        record.MarkCaptured("cap-1");
        context.Payments.Add(record);
        await context.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task Captured_payment_is_refunded_through_provider_and_PaymentRefunded_is_enqueued()
    {
        var orderId = Guid.NewGuid();
        await using var context = NewContext();
        await SeedCapturedPaymentAsync(context, orderId);

        var provider = Substitute.For<IPaymentProvider>();
        provider.RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var factory = Substitute.For<IPaymentProviderFactory>();
        factory.GetProvider(Method).Returns(provider);

        await NewProcessor().ProcessConfirmationFailureAsync(
            NewEvent(orderId), context, UnprocessedInbox(), factory, CancellationToken.None);
        await context.SaveChangesAsync();

        await provider.Received(1).RefundAsync("cap-1", 49.99m, "ocf", Arg.Any<CancellationToken>());

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(orderId));
        record.Status.Should().Be(PaymentStatus.Refunded);

        var outbox = await context.OutboxMessages
            .Where(m => m.Type == nameof(PaymentRefundedIntegrationEvent))
            .ToListAsync();
        outbox.Should().HaveCount(1);
    }

    [Fact]
    public async Task BypassProviders_refunds_without_contacting_provider()
    {
        var orderId = Guid.NewGuid();
        await using var context = NewContext();
        await SeedCapturedPaymentAsync(context, orderId);

        var factory = Substitute.For<IPaymentProviderFactory>();

        await NewProcessor(bypassProviders: true).ProcessConfirmationFailureAsync(
            NewEvent(orderId), context, UnprocessedInbox(), factory, CancellationToken.None);
        await context.SaveChangesAsync();

        factory.DidNotReceive().GetProvider(Arg.Any<string>());

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(orderId));
        record.Status.Should().Be(PaymentStatus.Refunded);

        (await context.OutboxMessages.CountAsync(m => m.Type == nameof(PaymentRefundedIntegrationEvent)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Provider_refund_failure_throws_so_the_message_is_retried()
    {
        var orderId = Guid.NewGuid();
        await using var context = NewContext();
        await SeedCapturedPaymentAsync(context, orderId);

        var provider = Substitute.For<IPaymentProvider>();
        provider.RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("Payment.Refund", "Gateway timeout.")));
        var factory = Substitute.For<IPaymentProviderFactory>();
        factory.GetProvider(Method).Returns(provider);

        var act = async () => await NewProcessor().ProcessConfirmationFailureAsync(
            NewEvent(orderId), context, UnprocessedInbox(), factory, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(orderId));
        record.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task Missing_payment_is_marked_processed_without_a_refund_event()
    {
        var orderId = Guid.NewGuid();
        await using var context = NewContext();
        var inbox = UnprocessedInbox();

        await NewProcessor().ProcessConfirmationFailureAsync(
            NewEvent(orderId), context, inbox, Substitute.For<IPaymentProviderFactory>(), CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.OutboxMessages.CountAsync(m => m.Type == nameof(PaymentRefundedIntegrationEvent)))
            .Should().Be(0);
        await inbox.Received(1).MarkAsProcessedAsync(
            Arg.Any<Guid>(), nameof(OrderConfirmationFailedIntegrationEvent), Arg.Any<CancellationToken>());
    }
}
