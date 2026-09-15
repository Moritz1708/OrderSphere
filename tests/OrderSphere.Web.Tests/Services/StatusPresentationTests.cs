namespace OrderSphere.Web.Tests.Services;

public sealed class StatusPresentationTests
{
    [Theory]
    [InlineData("Created", StatusTone.Warning, "OrderStatus.Created")]
    [InlineData("Pending", StatusTone.Warning, "OrderStatus.Pending")]
    [InlineData("Paid", StatusTone.Info, "OrderStatus.Paid")]
    [InlineData("Processing", StatusTone.Info, "OrderStatus.Processing")]
    [InlineData("Shipped", StatusTone.Accent, "OrderStatus.Shipped")]
    [InlineData("Delivered", StatusTone.Success, "OrderStatus.Delivered")]
    [InlineData("Cancelled", StatusTone.Danger, "OrderStatus.Cancelled")]
    public void OrderStatus_MapsToToneAndKey(string status, StatusTone tone, string key)
    {
        StatusPresentation.OrderTone(status).Should().Be(tone);
        StatusPresentation.OrderLabelKey(status).Should().Be(key);
    }

    [Fact]
    public void UnknownOrderStatus_IsNeutral_AndShowsTheRawValue()
    {
        // A new backend status must render readably, never blank.
        StatusPresentation.OrderTone("Refunded").Should().Be(StatusTone.Neutral);
        StatusPresentation.OrderLabelKey("Refunded").Should().Be("Refunded");
        StatusPresentation.OrderLabelKey(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Issued", StatusTone.Success)]
    [InlineData("Adjusted", StatusTone.Warning)]
    [InlineData("CreditIssued", StatusTone.Info)]
    [InlineData("Whatever", StatusTone.Neutral)]
    public void InvoiceStatus_MapsToTone(string status, StatusTone tone) =>
        StatusPresentation.InvoiceTone(status).Should().Be(tone);

    [Theory]
    [InlineData("Approved", StatusTone.Success, "Admin.Reviews.StatusApproved")]
    [InlineData("Rejected", StatusTone.Danger, "Admin.Reviews.StatusRejected")]
    [InlineData("Pending", StatusTone.Neutral, "Admin.Reviews.StatusPending")]
    public void ReviewStatus_MapsToToneAndKey(string status, StatusTone tone, string key)
    {
        StatusPresentation.ReviewTone(status).Should().Be(tone);
        StatusPresentation.ReviewLabelKey(status).Should().Be(key);
    }

    [Theory]
    [InlineData(0, StatusTone.Danger, "Stock.Unavailable")]
    [InlineData(-3, StatusTone.Danger, "Stock.Unavailable")]
    [InlineData(1, StatusTone.Warning, "Stock.OnlyLeft")]
    [InlineData(10, StatusTone.Warning, "Stock.OnlyLeft")]
    [InlineData(11, StatusTone.Success, "Stock.InStock")]
    public void Stock_UsesTheLowThreshold(int stock, StatusTone tone, string key)
    {
        StatusPresentation.StockTone(stock).Should().Be(tone);
        StatusPresentation.StockLabelKey(stock).Should().Be(key);
    }

    [Fact]
    public void Stock_ThresholdIsConfigurable()
    {
        StatusPresentation.StockTone(4, lowThreshold: 3).Should().Be(StatusTone.Success);
        StatusPresentation.StockTone(3, lowThreshold: 3).Should().Be(StatusTone.Warning);
    }

    [Fact]
    public void ActiveFlag_MapsToSuccessOrNeutral()
    {
        StatusPresentation.ActiveTone(true).Should().Be(StatusTone.Success);
        StatusPresentation.ActiveLabelKey(true).Should().Be("Common.Active");
        StatusPresentation.ActiveTone(false).Should().Be(StatusTone.Neutral);
        StatusPresentation.ActiveLabelKey(false).Should().Be("Common.Inactive");
    }
}
