using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSphere.Payment.Infrastructure.Providers;
using Xunit;

namespace OrderSphere.Payment.Tests;

/// <summary>
/// The simulated payment providers and the method-name resolution in the factory.
/// Providers are placeholders for real SDK integrations; the contract under test is the
/// transaction-id prefix per method and the success Result they return.
/// </summary>
public sealed class PaymentProviderTests
{
    private static readonly PaymentRequest Request =
        new(Guid.NewGuid(), 49.99m, "EUR", "customer@example.com", Guid.Empty, Guid.NewGuid());

    private static IPaymentProvider CreditCard() =>
        new CreditCardPaymentProvider(NullLogger<CreditCardPaymentProvider>.Instance);
    private static IPaymentProvider Invoice() =>
        new InvoicePaymentProvider(NullLogger<InvoicePaymentProvider>.Instance);
    private static IPaymentProvider PayPal() =>
        new PayPalPaymentProvider(NullLogger<PayPalPaymentProvider>.Instance);

    /// <summary>Provider paired with its method name.</summary>
    public static TheoryData<IPaymentProvider, string> ByMethodName() => new()
    {
        { CreditCard(), "CreditCard" },
        { Invoice(), "Invoice" },
        { PayPal(), "PayPal" },
    };

    /// <summary>Provider paired with its transaction-id prefix.</summary>
    public static TheoryData<IPaymentProvider, string> ByTransactionPrefix() => new()
    {
        { CreditCard(), "CC-" },
        { Invoice(), "INV-" },
        { PayPal(), "PP-" },
    };

    public static TheoryData<IPaymentProvider> AllProviders() => new()
    {
        CreditCard(), Invoice(), PayPal(),
    };

    [Theory]
    [MemberData(nameof(ByMethodName))]
    public void MethodName_MatchesProvider(IPaymentProvider provider, string method)
        => provider.MethodName.Should().Be(method);

    [Theory]
    [MemberData(nameof(ByTransactionPrefix))]
    public async Task Authorize_ReturnsSuccess_WithPrefixedTransactionId(
        IPaymentProvider provider, string prefix)
    {
        var result = await provider.AuthorizeAsync(Request);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionId.Should().StartWith(prefix);
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task Capture_ReturnsSuccess_EchoingTransactionId(IPaymentProvider provider)
    {
        var result = await provider.CaptureAsync("txn-123", 49.99m);

        result.IsSuccess.Should().BeTrue();
        result.Value.TransactionId.Should().Be("txn-123");
    }

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task Refund_ReturnsSuccess(IPaymentProvider provider)
        => (await provider.RefundAsync("txn-123", 49.99m, "ref-1")).IsSuccess.Should().BeTrue();

    [Theory]
    [MemberData(nameof(AllProviders))]
    public async Task Void_ReturnsSuccess(IPaymentProvider provider)
        => (await provider.VoidAsync("txn-123")).IsSuccess.Should().BeTrue();


    private static PaymentProviderFactory BuildFactory() =>
        new([CreditCard(), Invoice(), PayPal()]);

    [Theory]
    [InlineData("CreditCard")]
    [InlineData("Invoice")]
    [InlineData("PayPal")]
    public void Factory_ResolvesKnownMethod(string method)
        => BuildFactory().GetProvider(method).Should().NotBeNull();

    [Fact]
    public void Factory_IsCaseInsensitive()
        => BuildFactory().GetProvider("creditcard").Should().BeOfType<CreditCardPaymentProvider>();

    [Fact]
    public void Factory_UnknownMethod_ReturnsNull()
        => BuildFactory().GetProvider("bitcoin").Should().BeNull();
}
