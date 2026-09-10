namespace OrderSphere.Web.Services;

/// <summary>Visual weight of a status, independent of which domain it came from.</summary>
public enum StatusTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
    Accent,
}

/// <summary>
/// Maps domain status strings to a tone and a resource key. Previously each page
/// carried its own switch, and they disagreed — "Shipped" was Primary in the
/// customer view and Warning in the admin view. This is the single mapping.
/// <para>
/// Callers resolve the returned key through <c>IStringLocalizer</c>; an unknown
/// status yields <see cref="StatusTone.Neutral"/> and the raw string, so a new
/// backend status shows up readably instead of blank.
/// </para>
/// </summary>
public static class StatusPresentation
{
    /// <summary>Order lifecycle: Created, Pending, Paid, Processing, Shipped, Delivered, Cancelled.</summary>
    public static StatusTone OrderTone(string? status) => status switch
    {
        "Created" or "Pending" => StatusTone.Warning,
        "Paid" or "Processing" => StatusTone.Info,
        "Shipped" => StatusTone.Accent,
        "Delivered" => StatusTone.Success,
        "Cancelled" => StatusTone.Danger,
        _ => StatusTone.Neutral,
    };

    /// <summary>Resource key for an order status, or the raw value when unknown.</summary>
    public static string OrderLabelKey(string? status) => status switch
    {
        "Created" => "OrderStatus.Created",
        "Pending" => "OrderStatus.Pending",
        "Paid" => "OrderStatus.Paid",
        "Processing" => "OrderStatus.Processing",
        "Shipped" => "OrderStatus.Shipped",
        "Delivered" => "OrderStatus.Delivered",
        "Cancelled" => "OrderStatus.Cancelled",
        _ => status ?? string.Empty,
    };

    /// <summary>Invoice lifecycle: Issued, Adjusted, CreditIssued.</summary>
    public static StatusTone InvoiceTone(string? status) => status switch
    {
        "Issued" => StatusTone.Success,
        "Adjusted" => StatusTone.Warning,
        "CreditIssued" => StatusTone.Info,
        _ => StatusTone.Neutral,
    };

    public static string InvoiceLabelKey(string? status) => status switch
    {
        "Issued" => "Admin.Invoices.StatusIssued",
        "Adjusted" => "Admin.Invoices.StatusAdjusted",
        "CreditIssued" => "Admin.Invoices.StatusCreditIssued",
        _ => status ?? string.Empty,
    };

    /// <summary>Review moderation: Approved, Rejected, anything else is pending.</summary>
    public static StatusTone ReviewTone(string? status) => status switch
    {
        "Approved" => StatusTone.Success,
        "Rejected" => StatusTone.Danger,
        _ => StatusTone.Neutral,
    };

    public static string ReviewLabelKey(string? status) => status switch
    {
        "Approved" => "Admin.Reviews.StatusApproved",
        "Rejected" => "Admin.Reviews.StatusRejected",
        _ => "Admin.Reviews.StatusPending",
    };

    public static StatusTone ActiveTone(bool isActive) =>
        isActive ? StatusTone.Success : StatusTone.Neutral;

    public static string ActiveLabelKey(bool isActive) =>
        isActive ? "Common.Active" : "Common.Inactive";

    /// <summary>Stock level. <paramref name="lowThreshold"/> matches the admin low-stock view.</summary>
    public static StatusTone StockTone(int stock, int lowThreshold = 10) => stock switch
    {
        <= 0 => StatusTone.Danger,
        var s when s <= lowThreshold => StatusTone.Warning,
        _ => StatusTone.Success,
    };

    /// <summary>
    /// Resource key for a stock level. <c>Stock.OnlyLeft</c> takes the remaining
    /// count as a format argument, so callers pass <paramref name="stock"/> along.
    /// </summary>
    public static string StockLabelKey(int stock, int lowThreshold = 10) => stock switch
    {
        <= 0 => "Stock.Unavailable",
        var s when s <= lowThreshold => "Stock.OnlyLeft",
        _ => "Stock.InStock",
    };
}
