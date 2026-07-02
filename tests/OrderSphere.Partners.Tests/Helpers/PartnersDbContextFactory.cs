using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Partners.Infrastructure.Persistence;

namespace OrderSphere.Partners.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="PartnersDbContext"/> backed by a SQLite in-memory database,
/// so tests exercise the real model — including the global soft-delete and tenant query filters.
/// </summary>
internal static class PartnersDbContextFactory
{
    internal static PartnersDbContext Create(ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return Create(connection, tenantContext);
    }

    internal static PartnersDbContext Create(SqliteConnection connection, ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<PartnersDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new PartnersDbContext(options, tenantContext ?? NullTenantContext.Instance);
        context.Database.EnsureCreated();
        return context;
    }
}
