using FluentAssertions;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Domain.DomainEvents;
using OrderSphere.Payment.Domain.Entities;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Domain.Errors;
using Xunit;

namespace OrderSphere.Domain.Tests.Aggregates;

public sealed class PaymentRecordTests
{
    private static readonly OrderId Order = OrderId.New();

    private static PaymentRecord CreateRecord()
        => new(Order, 99.99m, "EUR", "CreditCard", "test@example.com", Guid.NewGuid());


    [Fact]
    public void Constructor_SetsStatusPending()
    {
        var record = CreateRecord();

        record.Status.Should().Be(PaymentStatus.Pending);
    }


    [Fact]
    public void MarkAuthorized_SetsStatusAuthorized()
    {
        var record = CreateRecord();

        record.MarkAuthorized("TXN-001");

        record.Status.Should().Be(PaymentStatus.Authorized);
        record.TransactionId.Should().Be("TXN-001");
    }

    [Fact]
    public void MarkAuthorized_RaisesPaymentAuthorizedDomainEvent()
    {
        var record = CreateRecord();

        record.MarkAuthorized("TXN-001");

        var events = record.PopDomainEvents();
        events.Should().ContainSingle()
            .Which.Should().BeOfType<PaymentAuthorizedDomainEvent>()
            .Which.TransactionId.Should().Be("TXN-001");
    }


    [Fact]
    public void MarkCaptured_SetsStatusCaptured()
    {
        var record = CreateRecord();
        record.MarkAuthorized("TXN-001");
        record.PopDomainEvents();

        record.MarkCaptured("TXN-001");

        record.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public void MarkCaptured_RaisesPaymentCapturedDomainEvent()
    {
        var record = CreateRecord();
        record.MarkAuthorized("TXN-001");
        record.PopDomainEvents();

        record.MarkCaptured("TXN-001");

        record.PopDomainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<PaymentCapturedDomainEvent>();
    }


    [Fact]
    public void MarkFailed_SetsStatusFailed()
    {
        var record = CreateRecord();

        record.MarkFailed("Insufficient funds");

        record.Status.Should().Be(PaymentStatus.Failed);
        record.FailureReason.Should().Be("Insufficient funds");
    }

    [Fact]
    public void MarkFailed_RaisesPaymentFailedDomainEvent()
    {
        var record = CreateRecord();

        record.MarkFailed("Insufficient funds");

        var events = record.PopDomainEvents();
        events.Should().ContainSingle()
            .Which.Should().BeOfType<PaymentFailedDomainEvent>()
            .Which.Reason.Should().Be("Insufficient funds");
    }


    [Fact]
    public void MarkRefunded_SetsStatusRefunded()
    {
        var record = CreateRecord();
        record.MarkCaptured("TXN-001");
        record.PopDomainEvents();

        record.MarkRefunded();

        record.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void MarkRefunded_DoesNotRaiseDomainEvent()
    {
        var record = CreateRecord();
        record.MarkCaptured("TXN-001");
        record.PopDomainEvents();

        record.MarkRefunded();

        record.PopDomainEvents().Should().BeEmpty();
    }


    [Fact]
    public void MarkAuthorized_EventContainsOrderId()
    {
        var record = CreateRecord();

        record.MarkAuthorized("TXN-001");

        var @event = record.PopDomainEvents().OfType<PaymentAuthorizedDomainEvent>().Single();
        @event.OrderId.Should().Be(Order);
    }

    [Fact]
    public void MarkCaptured_FromFailed_IsRejected()
    {
        var record = CreateRecord();
        record.MarkAuthorized("TXN-001");
        record.MarkFailed("Capture rejected.");

        var result = record.MarkCaptured("TXN-001");

        result.Error.Should().Be(PaymentErrors.InvalidStatusTransition);
        record.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void MarkCaptured_Repeated_WithSameTransaction_SucceedsWithoutNewEvent()
    {
        var record = CreateRecord();
        record.MarkCaptured("TXN-001");
        record.PopDomainEvents();

        var result = record.MarkCaptured("TXN-001");

        result.IsSuccess.Should().BeTrue();
        record.PopDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void MarkCaptured_Repeated_WithOtherTransaction_IsRejected()
    {
        var record = CreateRecord();
        record.MarkCaptured("TXN-001");

        var result = record.MarkCaptured("TXN-002");

        result.IsFailure.Should().BeTrue();
        record.TransactionId.Should().Be("TXN-001");
    }

    [Fact]
    public void MarkCaptured_MayReplaceTheAuthorizationReference()
    {
        var record = CreateRecord();
        record.MarkAuthorized("AUTH-1");

        var result = record.MarkCaptured("CAP-1");

        result.IsSuccess.Should().BeTrue();
        record.TransactionId.Should().Be("CAP-1");
    }

    [Fact]
    public void MarkFailed_FromCaptured_IsRejected()
    {
        var record = CreateRecord();
        record.MarkCaptured("TXN-001");

        var result = record.MarkFailed("late failure notice");

        result.IsFailure.Should().BeTrue();
        record.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public void MarkRefunded_BeforeCapture_IsRejected()
    {
        var record = CreateRecord();
        record.MarkAuthorized("TXN-001");

        var result = record.MarkRefunded();

        result.IsFailure.Should().BeTrue();
        record.Status.Should().Be(PaymentStatus.Authorized);
    }

    [Fact]
    public void MarkRefunded_Repeated_Succeeds()
    {
        var record = CreateRecord();
        record.MarkCaptured("TXN-001");
        record.MarkRefunded();

        record.MarkRefunded().IsSuccess.Should().BeTrue();
    }
}
