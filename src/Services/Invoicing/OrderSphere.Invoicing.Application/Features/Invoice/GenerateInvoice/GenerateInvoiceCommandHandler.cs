using InvoiceEntity = OrderSphere.Invoicing.Domain.Entities.Invoice;

namespace OrderSphere.Invoicing.Application.Features.Invoice.GenerateInvoice;

public sealed record GenerateInvoiceCommand(
    Guid OrderId,
    string CustomerEmail,
    string CustomerName,
    decimal Total,
    IReadOnlyList<InvoiceItemDto> Items) : ICommand<Result<InvoiceCreatedDto>>;

public sealed class GenerateInvoiceCommandValidator : AbstractValidator<GenerateInvoiceCommand>
{
    public GenerateInvoiceCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.CustomerEmail).NotEmpty();
        RuleFor(x => x.CustomerName).NotEmpty();
        RuleFor(x => x.Total).GreaterThan(0);
    }
}

public sealed class GenerateInvoiceCommandHandler(
    IInvoicingDbContext context,
    IInvoiceNumberGenerator numberGenerator,
    IInvoicePdfService pdfService,
    IBlobStorageService blobStorage,
    IInvoiceTaxRateProvider taxRateProvider) : ICommandHandler<GenerateInvoiceCommand, Result<InvoiceCreatedDto>>
{
    public async Task<Result<InvoiceCreatedDto>> Handle(GenerateInvoiceCommand request, CancellationToken ct)
    {
        var existing = await context.Invoices
            .FirstOrDefaultAsync(i => i.OrderId == request.OrderId, ct);

        if (existing is not null)
        {
            // Idempotent re-delivery: never draw a new number for an order that already has an invoice.
            var sasUrl = existing.BlobPath.Length > 0
                ? await blobStorage.GetSasUrlAsync(existing.BlobPath, ct)
                : string.Empty;
            return Result<InvoiceCreatedDto>.Success(new InvoiceCreatedDto(existing.InvoiceNumber, sasUrl));
        }

        var lineItems = request.Items
            .Select(i => new InvoiceLineItem { ProductName = i.ProductName, Quantity = i.Quantity, UnitPrice = i.UnitPrice })
            .ToList();

        var issuedAt = DateTime.UtcNow;

        // The number draw and the invoice insert share one transaction so the counter row stays locked
        // until commit — concurrent generations serialise and a rollback reverts the increment, keeping
        // numbering gapless. PDF render / blob upload run inside the transaction too; the row lock is
        // brief given the low consumer concurrency (InvoiceProcessor MaxConcurrentCalls = 2). The whole
        // delegate is retried as a unit by the Npgsql execution strategy on transient failures.
        var invoice = await context.ExecuteInTransactionAsync(async innerCt =>
        {
            var invoiceNumber = await numberGenerator.NextAsync(issuedAt, innerCt);

            var newInvoice = InvoiceEntity.Create(
                request.OrderId, request.CustomerEmail, request.CustomerName, request.Total,
                lineItems, invoiceNumber, issuedAt, taxRateProvider.DefaultRate);

            var pdfBytes = await pdfService.GenerateAsync(newInvoice, innerCt);

            if (blobStorage.IsEnabled)
            {
                var blobName = $"invoices/{newInvoice.OrderId}/{newInvoice.InvoiceNumber}.pdf";
                using var stream = new MemoryStream(pdfBytes);
                var blobPath = await blobStorage.UploadAsync(blobName, stream, "application/pdf", innerCt);
                newInvoice.SetBlobPath(blobPath);
            }

            context.Invoices.Add(newInvoice);
            return newInvoice;
        }, ct);

        var downloadUrl = invoice.BlobPath.Length > 0
            ? await blobStorage.GetSasUrlAsync(invoice.BlobPath, ct)
            : string.Empty;

        return Result<InvoiceCreatedDto>.Success(new InvoiceCreatedDto(invoice.InvoiceNumber, downloadUrl));
    }
}
