using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Webhooks.Infrastructure.Persistence;

namespace OrderSphere.Webhooks.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="WebhooksDbContext"/> backed by a SQLite in-memory database,
/// so tests exercise the real model — including the global soft-delete query filter.
/// </summary>
internal static class WebhooksDbContextFactory
{
    internal static WebhooksDbContext Create(ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new WebhooksDbContext(
            options, NullPublisher.Instance, tenantContext ?? NullTenantContext.Instance);
        context.Database.EnsureCreated();
        return context;
    }
}
