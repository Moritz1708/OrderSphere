namespace OrderSphere.UserProfile.Application.Abstractions;

public interface IUserProfileDbContext
{
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<SavedAddress> SavedAddresses { get; }
    DbSet<Tenant> Tenants { get; }

    /// <summary>
    /// Stages an integration event in the outbox table; dispatched to Service Bus by the
    /// OutboxDispatcher after the surrounding SaveChanges commits.
    /// </summary>
    void AddOutboxMessage(string type, string content);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
