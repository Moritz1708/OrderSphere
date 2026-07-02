using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using Xunit;

namespace OrderSphere.EventBus.AzureServiceBus.Tests.Scheduling;

public sealed class AuditLogRetentionJobTests
{
    [Fact]
    public async Task RunAsync_DeletesOnlyEntriesOlderThanTheRetentionWindow()
    {
        await using var context = SchedulingTestDbContext.Create();
        var now = DateTime.UtcNow;

        context.Set<AuditLogEntry>().AddRange(
            NewEntry("old-entity", now.AddDays(-200)),
            NewEntry("recent-entity", now.AddDays(-10)));
        await context.SaveChangesAsync();

        var job = new AuditLogRetentionJob<SchedulingTestDbContext>(
            context, EmptyConfiguration(), NullLogger<AuditLogRetentionJob<SchedulingTestDbContext>>.Instance);

        await job.RunAsync(CancellationToken.None);

        var remaining = context.Set<AuditLogEntry>().Select(e => e.EntityType).ToList();
        remaining.Should().BeEquivalentTo(["recent-entity"]);
    }

    [Fact]
    public async Task RunAsync_HonoursTheConfiguredRetentionDays()
    {
        await using var context = SchedulingTestDbContext.Create();
        context.Set<AuditLogEntry>().Add(NewEntry("thirty-days-old", DateTime.UtcNow.AddDays(-30)));
        await context.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Retention:AuditLogDays", "20")])
            .Build();

        var job = new AuditLogRetentionJob<SchedulingTestDbContext>(
            context, configuration, NullLogger<AuditLogRetentionJob<SchedulingTestDbContext>>.Instance);

        await job.RunAsync(CancellationToken.None);

        context.Set<AuditLogEntry>().Should().BeEmpty();
    }

    private static AuditLogEntry NewEntry(string entityType, DateTime occurredAt) => new()
    {
        EntityType = entityType,
        EntityId = Guid.NewGuid().ToString(),
        Action = AuditAction.Modified,
        OccurredAt = occurredAt,
        Changes = "{}"
    };

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();
}
