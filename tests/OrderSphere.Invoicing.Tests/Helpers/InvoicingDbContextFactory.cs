using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Invoicing.Infrastructure.Persistence;

namespace OrderSphere.Invoicing.Tests.Helpers;

/// <summary>
/// Builds <see cref="InvoicingDbContext"/> instances over a SQLite in-memory database. A relational
/// provider is used (not the EF in-memory provider) so the seeded <c>InvoiceNumberCounters</c> row and
/// transaction semantics behave like production. Pass a shared open connection to keep the database
/// alive across multiple context instances (e.g. to simulate separate message-processing attempts).
/// </summary>
internal static class InvoicingDbContextFactory
{
    internal static SqliteConnection NewOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    internal static InvoicingDbContext Create(SqliteConnection connection, ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<InvoicingDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new InvoicingDbContext(
            options, NullCurrentUser.Instance, tenantContext ?? NullTenantContext.Instance);
        context.Database.EnsureCreated();
        return context;
    }
}
