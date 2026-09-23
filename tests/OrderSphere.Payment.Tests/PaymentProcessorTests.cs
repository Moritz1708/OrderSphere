using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Domain.Entities;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;
using OrderSphere.Payment.Worker.Workers;
using Xunit;

namespace OrderSphere.Payment.Tests;

public sealed class PaymentProcessorTests
{
    private const string Method = "creditcard";

    private static PaymentDbContext NewContext() => Helpers.PaymentDbContextFactory.Create();

    private static PaymentProcessor NewProcessor(bool bypassProviders = false)
        => new(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new PaymentOptions { BypassProviders = bypassProviders }),
            NullLogger<PaymentProcessor>.Instance);

    private static PaymentRequestedIntegrationEvent NewEvent(string method = Method)
        => new()
        {
            OrderId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            Amount = 49.99m,
            Currency = "EUR",
            PaymentMethod = method,
            CustomerEmail = "customer@example.com"
        };

    private static IPaymentProviderFactory FactoryReturning(IPaymentProvider? provider, string method = Method)
    {
        var factory = Substitute.For<IPaymentProviderFactory>();
        factory.GetProvider(method).Returns(provider);
        return factory;
    }

    private static IPaymentProvider AuthorizingProvider(string authorizationId = "auth-1")
    {
        var provider = Substitute.For<IPaymentProvider>();
        provider.AuthorizeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PaymentProviderResult>.Success(new PaymentProviderResult(authorizationId)));
        provider.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return provider;
    }

    [Fact]
    public async Task Authorize_and_capture_succeed_persists_captured_record()
    {
        await using var context = NewContext();
        var evt = NewEvent();

        var provider = AuthorizingProvider();
        provider.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result<PaymentProviderResult>.Success(new PaymentProviderResult("cap-1")));

        var result = await NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider), CancellationToken.None);
        await context.SaveChangesAsync(); // mirrors the single Save in OnMessageReceived

        result.Status.Should().Be(PaymentStatus.Captured);

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(evt.OrderId));
        record.Status.Should().Be(PaymentStatus.Captured);
        record.TransactionId.Should().Be("cap-1");
    }

    [Fact]
    public async Task Provider_request_carries_tenant_and_correlation_for_the_idempotency_scope()
    {
        await using var context = NewContext();
        var evt = NewEvent();
        var provider = AuthorizingProvider();
        provider.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result<PaymentProviderResult>.Success(new PaymentProviderResult("auth-1")));

        await NewProcessor().ProcessPaymentAsync(evt, context, FactoryReturning(provider), CancellationToken.None);

        await provider.Received(1).AuthorizeAsync(
            Arg.Is<PaymentRequest>(r => r.OrderId == evt.OrderId
                && r.CorrelationId == evt.CorrelationId
                && r.TenantId == evt.TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unsupported_payment_method_persists_failed_record()
    {
        await using var context = NewContext();
        var evt = NewEvent("bitcoin");

        var result = await NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider: null, method: "bitcoin"), CancellationToken.None);
        await context.SaveChangesAsync();

        result.Status.Should().Be(PaymentStatus.Failed);

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(evt.OrderId));
        record.Status.Should().Be(PaymentStatus.Failed);
        record.FailureReason.Should().Contain("bitcoin");
    }

    [Fact]
    public async Task Authorization_failure_persists_failed_record_without_capture()
    {
        await using var context = NewContext();
        var evt = NewEvent();

        var provider = Substitute.For<IPaymentProvider>();
        provider.AuthorizeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PaymentProviderResult>.Failure(new Error("Payment.Auth", "Card declined.")));

        var result = await NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider), CancellationToken.None);
        await context.SaveChangesAsync();

        result.Status.Should().Be(PaymentStatus.Failed);

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(evt.OrderId));
        record.Status.Should().Be(PaymentStatus.Failed);
        record.FailureReason.Should().Be("Card declined.");
        await provider.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Capture_failure_voids_the_authorization_and_keeps_its_reference()
    {
        await using var context = NewContext();
        var evt = NewEvent();

        var provider = AuthorizingProvider("auth-1");
        provider.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result<PaymentProviderResult>.Failure(new Error("Payment.Capture", "Capture rejected.")));

        var result = await NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider), CancellationToken.None);
        await context.SaveChangesAsync();

        result.Status.Should().Be(PaymentStatus.Failed);
        await provider.Received(1).VoidAsync("auth-1", Arg.Any<CancellationToken>());

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(evt.OrderId));
        record.Status.Should().Be(PaymentStatus.Failed);
        record.FailureReason.Should().Be("Capture rejected.");
        record.TransactionId.Should().Be("auth-1", "the webhook reconciles by the provider reference");
    }

    [Fact]
    public async Task Failed_void_does_not_change_the_outcome()
    {
        await using var context = NewContext();
        var evt = NewEvent();

        var provider = AuthorizingProvider();
        provider.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Result<PaymentProviderResult>.Failure(new Error("Payment.Capture", "Capture rejected.")));
        provider.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("Payment.VoidFailed", "Cancel rejected.")));

        var result = await NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider), CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task Transient_provider_exception_propagates_and_persists_nothing()
    {
        await using var context = NewContext();
        var evt = NewEvent();

        var provider = AuthorizingProvider();
        provider.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var act = () => NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();

        // The worker abandons the message without saving; a fresh context sees no record.
        context.ChangeTracker.Clear();
        (await context.Payments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Existing_captured_payment_is_returned_without_contacting_the_provider()
    {
        await using var context = NewContext();
        var evt = NewEvent();

        var existing = new PaymentRecord(
            OrderId.From(evt.OrderId), evt.Amount, evt.Currency, evt.PaymentMethod, evt.CustomerEmail, evt.CorrelationId);
        existing.MarkCaptured("pre-existing");
        context.Payments.Add(existing);
        await context.SaveChangesAsync();

        var provider = Substitute.For<IPaymentProvider>();

        var result = await NewProcessor().ProcessPaymentAsync(
            evt, context, FactoryReturning(provider), CancellationToken.None);

        result.Status.Should().Be(PaymentStatus.Captured);
        (await context.Payments.CountAsync(p => p.OrderId == OrderId.From(evt.OrderId))).Should().Be(1);
        await provider.DidNotReceive().AuthorizeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaymentProcessed_event_carries_the_transaction_reference()
    {
        await using var context = NewContext();
        var evt = NewEvent();
        var record = new PaymentRecord(
            OrderId.From(evt.OrderId), evt.Amount, evt.Currency, evt.PaymentMethod, evt.CustomerEmail, evt.CorrelationId);
        record.MarkAuthorized("auth-1");
        record.MarkFailed("Capture rejected.");

        PaymentProcessor.EnqueuePaymentProcessedOutboxMessage(context, evt, record);
        await context.SaveChangesAsync();

        var json = await context.OutboxMessages.Select(m => m.Content).SingleAsync();
        var processed = System.Text.Json.JsonSerializer.Deserialize<PaymentProcessedIntegrationEvent>(json)!;
        processed.Succeeded.Should().BeFalse();
        processed.TransactionId.Should().Be("auth-1");
    }

    [Fact]
    public async Task BypassProviders_marks_payment_as_captured_without_contacting_provider()
    {
        await using var context = NewContext();
        var evt = NewEvent();
        var factory = Substitute.For<IPaymentProviderFactory>();

        var result = await NewProcessor(bypassProviders: true).ProcessPaymentAsync(
            evt, context, factory, CancellationToken.None);
        await context.SaveChangesAsync();

        result.Status.Should().Be(PaymentStatus.Captured);

        var record = await context.Payments.SingleAsync(p => p.OrderId == OrderId.From(evt.OrderId));
        record.Status.Should().Be(PaymentStatus.Captured);
        record.TransactionId.Should().StartWith("DEV-");

        factory.DidNotReceive().GetProvider(Arg.Any<string>());
    }
}
