using Azure.Messaging.ServiceBus;
using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Infrastructure.Persistence;
using OrderSphere.Ordering.Worker.Workers;
using Xunit;
using OrderItemDto = OrderSphere.BuildingBlocks.Contracts.Events.OrderItemDto;
using ShippingAddressDto = OrderSphere.BuildingBlocks.Contracts.Events.ShippingAddressDto;

namespace OrderSphere.IntegrationTests;

/// <summary>
/// Verifies the A1 saga read-model is written by the worker hops within the same
/// transactions that mutate the order: OrderProcessor advances it to
/// <see cref="SagaState.PaymentRequested"/>; PaymentResultProcessor advances it to
/// <see cref="SagaState.Confirmed"/> (success) or <see cref="SagaState.Cancelled"/> (failure).
///
/// Uses SQLite in-process (not EF InMemory) because OrderItem.Price (Money) is a
/// ComplexProperty the InMemory provider cannot shape.
/// </summary>
public sealed class OrderSagaProjectionTests : IDisposable
{
    private static readonly Guid CustomerGuid = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ProductGuid = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrderingDbContext> _options;

    public OrderSagaProjectionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private OrderingDbContext NewContext() =>
        new(_options, Substitute.For<IPublisher>(), NullCurrentUser.Instance, NullTenantContext.Instance);

    private static PaymentResultProcessor NewPaymentProcessor()
        => new(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<PaymentResultProcessor>.Instance);

    private static ICatalogClient Catalog()
    {
        var catalog = Substitute.For<ICatalogClient>();
        catalog.ConfirmReservationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        catalog.ReleaseReservationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        return catalog;
    }

    private async Task<(Guid OrderId, Guid CorrelationId)> SeedOrderAsync()
    {
        var checkoutEvent = new CheckoutCartIntegrationEvent
        {
            CorrelationId = Guid.NewGuid(),
            CustomerId = CustomerGuid,
            CustomerEmail = "customer@example.com",
            CustomerName = "Max Mustermann",
            ShippingAddress = new ShippingAddressDto("Max", "Mustermann", "Hauptstr. 1", "Berlin", "10115", "DE"),
            PaymentMethod = PaymentMethod.CreditCard.ToString(),
            Items = [new OrderItemDto(ProductGuid, "Widget", 2, 9.99m)]
        };

        await using var ctx = NewContext();
        var result = await new OrderProcessor(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IShippingRateProvider>(),
            NullLogger<OrderProcessor>.Instance)
            .ProcessOrderAsync(checkoutEvent, ctx, CancellationToken.None);

        if (!result.IsSuccess) throw new InvalidOperationException($"ProcessOrderAsync failed: {result.ErrorMessage}");

        var orderId = await ctx.Orders
            .Where(o => o.CorrelationId == checkoutEvent.CorrelationId)
            .Select(o => o.Id.Value)
            .SingleAsync();

        return (orderId, checkoutEvent.CorrelationId);
    }

    private static PaymentProcessedIntegrationEvent SucceededEvent(Guid orderId, Guid correlationId) => new()
    {
        OrderId = orderId,
        CorrelationId = correlationId,
        Succeeded = true,
        CustomerEmail = "customer@example.com",
        PaymentMethod = "creditcard"
    };

    private static PaymentProcessedIntegrationEvent FailedEvent(Guid orderId, Guid correlationId) => new()
    {
        OrderId = orderId,
        CorrelationId = correlationId,
        Succeeded = false,
        FailureReason = "Card declined.",
        CustomerEmail = "customer@example.com",
        PaymentMethod = "creditcard"
    };

    [Fact]
    public async Task OrderProcessor_starts_saga_at_PaymentRequested()
    {
        var (orderId, correlationId) = await SeedOrderAsync();

        await using var verify = NewContext();
        var saga = await verify.OrderSagas.SingleAsync(s => s.CorrelationId == correlationId);
        saga.State.Should().Be(SagaState.PaymentRequested);
        saga.OrderId.Should().Be(orderId);
        saga.PaymentRequestedAt.Should().NotBeNull();
        saga.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Successful_payment_advances_saga_to_Confirmed()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = SucceededEvent(orderId, correlationId);

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        await NewPaymentProcessor().ProcessPaymentResultAsync(evt, ctx, inbox, Catalog(), deliveryCount: 1, CancellationToken.None);
        await ctx.SaveChangesAsync();

        await using var verify = NewContext();
        var saga = await verify.OrderSagas.SingleAsync(s => s.CorrelationId == correlationId);
        saga.State.Should().Be(SagaState.Confirmed);
        saga.CompletedAt.Should().NotBeNull();
        saga.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Failed_payment_advances_saga_to_Cancelled_with_reason()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = FailedEvent(orderId, correlationId);

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        await NewPaymentProcessor().ProcessPaymentResultAsync(evt, ctx, inbox, Catalog(), deliveryCount: 1, CancellationToken.None);
        await ctx.SaveChangesAsync();

        await using var verify = NewContext();
        var saga = await verify.OrderSagas.SingleAsync(s => s.CorrelationId == correlationId);
        saga.State.Should().Be(SagaState.Cancelled);
        saga.CompletedAt.Should().NotBeNull();
        saga.LastError.Should().Be("Card declined.");
    }
}
