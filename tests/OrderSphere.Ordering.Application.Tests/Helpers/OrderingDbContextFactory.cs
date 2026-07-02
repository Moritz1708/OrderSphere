using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Application.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="OrderingDbContext"/> backed by a SQLite in-memory database.
/// SQLite is used rather than the EF in-memory provider because the latter does not reliably
/// apply global query filters on entities with complex properties (e.g. <c>OrderItem.Price</c>),
/// which these tests rely on to verify soft-delete exclusion.
/// </summary>
internal static class OrderingDbContextFactory
{
    internal static OrderingDbContext Create() => Create(NullCurrentUser.Instance);

    internal static OrderingDbContext Create(ICurrentUser currentUser, ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var context = Create(connection, currentUser, tenantContext);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Opens a new context over an already-open shared connection, without calling
    /// <c>EnsureCreated</c>. Use this to build multiple contexts (e.g. one per tenant) against the
    /// same in-memory database, mirroring how separate requests share one Postgres database.
    /// </summary>
    internal static OrderingDbContext Create(
        SqliteConnection connection, ICurrentUser? currentUser = null, ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseSqlite(connection)
            .Options;

        return new OrderingDbContext(
            options, NullPublisher.Instance, currentUser ?? NullCurrentUser.Instance,
            tenantContext ?? NullTenantContext.Instance);
    }
}
