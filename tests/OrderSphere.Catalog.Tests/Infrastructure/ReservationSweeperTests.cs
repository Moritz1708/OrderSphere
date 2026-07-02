using Microsoft.Extensions.Logging.Abstractions;
using OrderSphere.Catalog.Api.BackgroundServices;
using OrderSphere.Catalog.Domain.Enums;
using OrderSphere.Catalog.Tests.Helpers;

namespace OrderSphere.Catalog.Tests.Infrastructure;

public sealed class ReservationSweeperTests
{
    [Fact]
    public async Task RunAsync_ReleasesOnlyExpiredActiveReservations()
    {
        using var context = CatalogDbContextFactory.Create();
        var now = DateTime.UtcNow;

        var expired = new StockReservation(Guid.NewGuid(), ProductId.New(), 2, now.AddMinutes(-1));
        var notYetExpired = new StockReservation(Guid.NewGuid(), ProductId.New(), 1, now.AddMinutes(30));
        context.StockReservations.AddRange(expired, notYetExpired);
        await context.SaveChangesAsync();

        var sweeper = new ReservationSweeper(context, NullLogger<ReservationSweeper>.Instance);
        await sweeper.RunAsync(CancellationToken.None);

        (await context.StockReservations.FindAsync(expired.Id))!.Status.Should().Be(ReservationStatus.Released);
        (await context.StockReservations.FindAsync(notYetExpired.Id))!.Status.Should().Be(ReservationStatus.Active);
    }

    [Fact]
    public async Task RunAsync_LeavesAlreadyConfirmedReservationsUntouched()
    {
        using var context = CatalogDbContextFactory.Create();
        var confirmed = new StockReservation(Guid.NewGuid(), ProductId.New(), 3, DateTime.UtcNow.AddMinutes(-10));
        confirmed.Confirm();
        context.StockReservations.Add(confirmed);
        await context.SaveChangesAsync();

        var sweeper = new ReservationSweeper(context, NullLogger<ReservationSweeper>.Instance);
        await sweeper.RunAsync(CancellationToken.None);

        (await context.StockReservations.FindAsync(confirmed.Id))!.Status.Should().Be(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task RunAsync_WhenNothingIsExpired_DoesNotThrow()
    {
        using var context = CatalogDbContextFactory.Create();
        var sweeper = new ReservationSweeper(context, NullLogger<ReservationSweeper>.Instance);

        var act = async () => await sweeper.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
