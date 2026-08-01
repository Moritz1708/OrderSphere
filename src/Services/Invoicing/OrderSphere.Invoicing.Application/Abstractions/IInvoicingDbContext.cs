namespace OrderSphere.Invoicing.Application.Abstractions;

public interface IInvoicingDbContext
{
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceAdjustment> InvoiceAdjustments { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default);
}
