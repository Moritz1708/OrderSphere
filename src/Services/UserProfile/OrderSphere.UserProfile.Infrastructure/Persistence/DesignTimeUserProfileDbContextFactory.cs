using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.UserProfile.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by EF Core tooling (dotnet ef migrations add).
/// Not registered in the production DI container.
/// </summary>
public sealed class DesignTimeUserProfileDbContextFactory : IDesignTimeDbContextFactory<UserProfileDbContext>
{
    public UserProfileDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UserProfileDbContext>()
            .UseNpgsql("Host=localhost;Database=userprofile-db;Username=postgres;Password=postgres")
            .Options;

        return new UserProfileDbContext(options, NullPublisher.Instance, NullCurrentUser.Instance, NullTenantContext.Instance);
    }
}
