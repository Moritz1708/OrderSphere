using System.Text;
using System.Text.RegularExpressions;
using OrderSphere.Invoicing.Domain.Entities;
using OrderSphere.Invoicing.Infrastructure.Pdf;
using QuestPDF.Infrastructure;

namespace OrderSphere.Invoicing.Tests;

public sealed class QuestPdfInvoiceServiceTests
{
    private static readonly DateTime IssuedAt = new(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);

    public QuestPdfInvoiceServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Fail on any glyph the embedded fonts lack instead of silently drawing a placeholder.
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    private static Invoice CreateInvoice(int itemCount = 2)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(n => new InvoiceLineItem { ProductName = $"Größenverstellbarer Artikel {n}", Quantity = n, UnitPrice = 49.90m })
            .ToList();

        return Invoice.Create(
            Guid.NewGuid(), "ada@example.com", "Ada Lovelace",
            items.Sum(i => i.Quantity * i.UnitPrice), items,
            "INV-2026-000042", IssuedAt, taxRate: 0.19m);
    }

    // PDF font dictionaries are written uncompressed; subset names look like "ABCDEF+Manrope-Regular".
    private static IReadOnlyCollection<string> EmbeddedFonts(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/BaseFont\s*/[A-Z]{6}\+([A-Za-z0-9\-]+)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

    [Fact]
    public async Task GenerateAsync_embeds_the_design_system_fonts_and_no_fallback()
    {
        var pdf = await new QuestPdfInvoiceService().GenerateAsync(CreateInvoice());

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");

        var fonts = EmbeddedFonts(pdf);
        fonts.Should().Contain(["InstrumentSerif-Regular", "Manrope-Regular", "Manrope-SemiBold", "GeistMono-Regular"]);
        fonts.Should().OnlyContain(f => f.StartsWith("InstrumentSerif") || f.StartsWith("Manrope") || f.StartsWith("GeistMono"));
    }

    [Fact]
    public async Task GenerateAsync_renders_adjustments_and_long_item_lists_across_pages()
    {
        var invoice = CreateInvoice(itemCount: 40);
        invoice.ApplyDiscount(10m, "Kulanz", "admin@ordersphere.dev", IssuedAt.AddDays(2)).IsSuccess.Should().BeTrue();
        invoice.IssueCreditNote(5m, "Beschädigte Verpackung", "admin@ordersphere.dev", IssuedAt.AddDays(3)).IsSuccess.Should().BeTrue();

        var pdf = await new QuestPdfInvoiceService().GenerateAsync(invoice);

        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count.Should().BeGreaterThan(1);
    }
}
