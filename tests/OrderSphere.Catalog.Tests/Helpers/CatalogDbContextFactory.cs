using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Catalog.Infrastructure.Persistence;

namespace OrderSphere.Catalog.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="CatalogDbContext"/> backed by a SQLite in-memory database.
/// SQLite is used rather than the EF in-memory provider because the latter does not reliably
/// apply global query filters on entities with complex properties (e.g. <c>Product.Price</c>),
/// which these tests rely on to verify soft-delete exclusion.
/// </summary>
internal static class CatalogDbContextFactory
{
    internal static CatalogDbContext Create(ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new CatalogDbContext(
            options, NullPublisher.Instance, NullCurrentUser.Instance, tenantContext ?? NullTenantContext.Instance);
        context.Database.EnsureCreated();
        return context;
    }
}
