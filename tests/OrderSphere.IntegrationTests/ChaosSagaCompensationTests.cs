using System.Net;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Infrastructure.CatalogClient;
using OrderSphere.Ordering.Infrastructure.Persistence;
using OrderSphere.Ordering.Worker.Workers;
using Xunit;
using OrderItemDto = OrderSphere.BuildingBlocks.Contracts.Events.OrderItemDto;
using ShippingAddressDto = OrderSphere.BuildingBlocks.Contracts.Events.ShippingAddressDto;

namespace OrderSphere.IntegrationTests;

/// <summary>
/// Chaos-driven counterpart to <see cref="SagaCompensationTests.Confirm_conflict_compensates_and_requests_refund"/>.
/// Instead of mocking <see cref="ICatalogClient"/>, a real <see cref="HttpCatalogClient"/> runs behind
/// the opt-in chaos resilience pipeline (<c>AddOrderSphereChaos</c>), which deterministically
/// synthesizes an HTTP 409 in place of a live Catalog confirm call. This proves the saga compensates
/// correctly when the conflict arrives through the actual HTTP/resilience stack, not just a
/// hand-rolled <c>Result.Failure</c> — closing the gap between "the domain logic is correct" and
/// "the domain logic still behaves correctly once real transport failures reach it".
/// </summary>
public sealed class ChaosSagaCompensationTests : IDisposable
{
    private static readonly Guid CustomerGuid = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ProductGuid = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrderingDbContext> _options;

    public ChaosSagaCompensationTests()
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
        new(_options, Substitute.For<MediatR.IPublisher>(), NullCurrentUser.Instance, NullTenantContext.Instance);

    // A stand-in Catalog backend that would always succeed if the request actually reached it —
    // isolates the assertion to what the chaos pipeline itself injects.
    private sealed class AlwaysSuccessfulCatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    // Builds a real HttpCatalogClient wired through AddOrderSphereChaos with a deterministic
    // (InjectionRate 1.0) synthetic 409, matching how a service opts in via Chaos:* configuration.
    private static ICatalogClient NewChaosCatalogClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Chaos:Enabled"] = "true",
                ["Chaos:FaultInjectionRate"] = "1",
                ["Chaos:LatencyInjectionRate"] = "0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient<ICatalogClient, HttpCatalogClient>(client =>
            {
                client.BaseAddress = new Uri("https://ordersphere-catalog.internal");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new AlwaysSuccessfulCatalogHandler())
            .AddOrderSphereChaos(configuration);

        return services.BuildServiceProvider().GetRequiredService<ICatalogClient>();
    }

    // Chaos disabled (the default) — proves AddOrderSphereChaos is a true no-op and calling it
    // unconditionally on a client is safe.
    private static ICatalogClient NewNonChaosCatalogClient()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient<ICatalogClient, HttpCatalogClient>(client =>
            {
                client.BaseAddress = new Uri("https://ordersphere-catalog.internal");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new AlwaysSuccessfulCatalogHandler())
            .AddOrderSphereChaos(configuration);

        return services.BuildServiceProvider().GetRequiredService<ICatalogClient>();
    }

    private static PaymentResultProcessor NewPaymentResultProcessor()
        => new(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<PaymentResultProcessor>.Instance);

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

    [Fact]
    public async Task Chaos_injected_confirm_conflict_compensates_and_requests_refund()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = new PaymentProcessedIntegrationEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            Succeeded = true,
            CustomerEmail = "customer@example.com",
            PaymentMethod = "creditcard"
        };

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        var outcome = await NewPaymentResultProcessor()
            .ProcessPaymentResultAsync(evt, ctx, inbox, NewChaosCatalogClient(), deliveryCount: 1, CancellationToken.None);
        await ctx.SaveChangesAsync();

        outcome.Should().Be(PaymentResultProcessor.PaymentResultOutcome.Processed);

        await using var verify = NewContext();
        var saga = await verify.OrderSagas.SingleAsync(s => s.CorrelationId == correlationId);
        saga.State.Should().Be(SagaState.CompensationPending);
        saga.CompletedAt.Should().BeNull();

        var order = await verify.Orders.SingleAsync(o => o.Id == OrderId.From(orderId));
        order.Status.Should().Be(OrderStatus.Cancelled);

        var outbox = await verify.OutboxMessages
            .Where(m => m.Type != nameof(PaymentRequestedIntegrationEvent))
            .ToListAsync();
        outbox.Should().Contain(m => m.Type == nameof(OrderConfirmationFailedIntegrationEvent));
    }

    [Fact]
    public async Task Chaos_disabled_confirm_succeeds_and_saga_completes_normally()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = new PaymentProcessedIntegrationEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            Succeeded = true,
            CustomerEmail = "customer@example.com",
            PaymentMethod = "creditcard"
        };

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        var outcome = await NewPaymentResultProcessor()
            .ProcessPaymentResultAsync(evt, ctx, inbox, NewNonChaosCatalogClient(), deliveryCount: 1, CancellationToken.None);
        await ctx.SaveChangesAsync();

        outcome.Should().Be(PaymentResultProcessor.PaymentResultOutcome.Processed);

        await using var verify = NewContext();
        var saga = await verify.OrderSagas.SingleAsync(s => s.CorrelationId == correlationId);
        saga.State.Should().Be(SagaState.Confirmed);

        var order = await verify.Orders.SingleAsync(o => o.Id == OrderId.From(orderId));
        order.Status.Should().NotBe(OrderStatus.Cancelled);
    }
}
