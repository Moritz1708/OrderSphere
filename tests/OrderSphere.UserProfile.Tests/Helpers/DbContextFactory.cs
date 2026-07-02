using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.UserProfile.Infrastructure.Persistence;

namespace OrderSphere.UserProfile.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="UserProfileDbContext"/> backed by the EF Core in-memory provider.
/// Each call gets a fresh database name so tests do not share state.
/// </summary>
internal static class DbContextFactory
{
    internal static UserProfileDbContext Create(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<UserProfileDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new UserProfileDbContext(
            options, NullPublisher.Instance, NullCurrentUser.Instance, tenantContext ?? NullTenantContext.Instance);
    }
}
