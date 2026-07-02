using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.Inbox;

namespace OrderSphere.EventBus.AzureServiceBus.Tests.Scheduling;

/// <summary>
/// Minimal DbContext exposing <see cref="InboxMessage"/> and <see cref="AuditLogEntry"/>, the two
/// entity types the scheduled retention jobs delete from. Backed by SQLite in-memory so
/// <c>ExecuteDeleteAsync</c> (unsupported by the EF in-memory provider) works in tests.
/// </summary>
internal sealed class SchedulingTestDbContext(DbContextOptions<SchedulingTestDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(b => b.HasKey(m => m.EventId));
        modelBuilder.Entity<AuditLogEntry>(b => b.HasKey(e => e.Id));
    }

    internal static SchedulingTestDbContext Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SchedulingTestDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new SchedulingTestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
