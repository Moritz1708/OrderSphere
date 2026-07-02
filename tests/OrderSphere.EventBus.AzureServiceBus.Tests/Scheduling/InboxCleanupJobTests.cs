using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using Xunit;

namespace OrderSphere.EventBus.AzureServiceBus.Tests.Scheduling;

public sealed class InboxCleanupJobTests
{
    [Fact]
    public async Task RunAsync_DeletesOnlyMessagesOlderThanTheRetentionWindow()
    {
        await using var context = SchedulingTestDbContext.Create();
        var now = DateTime.UtcNow;

        context.Set<InboxMessage>().AddRange(
            new InboxMessage { EventId = Guid.NewGuid(), EventType = "old", ProcessedAt = now.AddDays(-40) },
            new InboxMessage { EventId = Guid.NewGuid(), EventType = "recent", ProcessedAt = now.AddDays(-5) });
        await context.SaveChangesAsync();

        var job = new InboxCleanupJob<SchedulingTestDbContext>(
            context, EmptyConfiguration(), NullLogger<InboxCleanupJob<SchedulingTestDbContext>>.Instance);

        await job.RunAsync(CancellationToken.None);

        var remaining = context.Set<InboxMessage>().Select(m => m.EventType).ToList();
        remaining.Should().BeEquivalentTo(["recent"]);
    }

    [Fact]
    public async Task RunAsync_HonoursTheConfiguredRetentionDays()
    {
        await using var context = SchedulingTestDbContext.Create();
        var now = DateTime.UtcNow;

        context.Set<InboxMessage>().Add(
            new InboxMessage { EventId = Guid.NewGuid(), EventType = "ten-days-old", ProcessedAt = now.AddDays(-10) });
        await context.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Retention:InboxDays", "5")])
            .Build();

        var job = new InboxCleanupJob<SchedulingTestDbContext>(
            context, configuration, NullLogger<InboxCleanupJob<SchedulingTestDbContext>>.Instance);

        await job.RunAsync(CancellationToken.None);

        context.Set<InboxMessage>().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_WhenNothingIsExpired_DeletesNothing()
    {
        await using var context = SchedulingTestDbContext.Create();
        context.Set<InboxMessage>().Add(
            new InboxMessage { EventId = Guid.NewGuid(), EventType = "fresh", ProcessedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var job = new InboxCleanupJob<SchedulingTestDbContext>(
            context, EmptyConfiguration(), NullLogger<InboxCleanupJob<SchedulingTestDbContext>>.Instance);

        await job.RunAsync(CancellationToken.None);

        context.Set<InboxMessage>().Should().HaveCount(1);
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();
}
