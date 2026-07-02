using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Scheduling;
using OrderSphere.Catalog.Domain.Enums;
using OrderSphere.Catalog.Infrastructure.Persistence;

namespace OrderSphere.Catalog.Api.BackgroundServices;

/// <summary>
/// Releases stock reservations whose TTL has elapsed, freeing availability for abandoned
/// checkouts. Runs in Catalog.Api so it shares the catalog database. Hosted via
/// <c>AddScheduledJob&lt;ReservationSweeper&gt;()</c> (ServiceDefaults).
/// </summary>
public sealed class ReservationSweeper(
    CatalogDbContext context,
    ILogger<ReservationSweeper> logger) : IScheduledJob
{
    public static TimeSpan Interval => TimeSpan.FromMinutes(1);
    public static string LockResource => "catalog:reservation-sweep";

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await context.StockReservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        foreach (var reservation in expired)
            reservation.Release();

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Released {Count} expired stock reservation(s).", expired.Count);
    }
}
