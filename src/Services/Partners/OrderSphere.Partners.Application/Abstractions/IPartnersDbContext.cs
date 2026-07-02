using OrderSphere.Partners.Domain.Entities;

namespace OrderSphere.Partners.Application.Abstractions;

public interface IPartnersDbContext
{
    DbSet<Partner> Partners { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
