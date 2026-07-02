namespace OrderSphere.Invoicing.Infrastructure.Persistence;

public sealed class DesignTimeInvoicingDbContextFactory : IDesignTimeDbContextFactory<InvoicingDbContext>
{
    public InvoicingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InvoicingDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=invoicing-db;Username=postgres;Password=postgres")
            .Options;
        return new InvoicingDbContext(options, NullCurrentUser.Instance, NullTenantContext.Instance);
    }
}
