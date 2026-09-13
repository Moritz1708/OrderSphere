using System.Globalization;
using OrderSphere.Invoicing.Domain.Enums;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrderSphere.Invoicing.Infrastructure.Pdf;

/// <summary>
/// Renders the customer invoice in the storefront's "Bold Editorial" style: serif display type,
/// Manrope for text, Geist Mono for every number, hairline rules and a single accent.
/// </summary>
public sealed class QuestPdfInvoiceService : IInvoicePdfService
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    // Literal light-mode values of the web design tokens (src/Frontend/OrderSphere.Web/Services/DesignTokens.cs).
    // Hairlines are the CSS color-mix(ink 12% / 24%) flattened onto white, since a PDF has no color-mix.
    private const string Ink = "#14120F";
    private const string Muted = "#6B6560";
    private const string Sunk = "#F7F5F0";
    private const string Accent = "#FF4D1F";
    private const string AccentInk = "#C93A0F";
    private const string Hairline = "#E3E2E2";
    private const string HairlineStrong = "#C9C8C7";

    private const string Display = "Instrument Serif";
    private const string Body = "Manrope";
    private const string Mono = "Geist Mono";

    // QuestPDF keeps registered fonts process-wide; register the embedded files exactly once.
    // Embedding (instead of relying on installed fonts) keeps the output identical in containers.
    private static readonly Lazy<bool> FontsRegistered = new(RegisterFonts);

    public Task<byte[]> GenerateAsync(Invoice invoice, CancellationToken ct = default) =>
        Task.FromResult(CreateDocument(invoice).GeneratePdf());

    internal static IDocument CreateDocument(Invoice invoice)
    {
        _ = FontsRegistered.Value;

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(18, Unit.Millimetre);
            page.MarginVertical(16, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily(Body).FontSize(9.5f).FontColor(Ink).LineHeight(1.35f));

            page.Header().Element(c => ComposeHeader(c, invoice));
            page.Content().PaddingTop(22).Element(c => ComposeContent(c, invoice));
            page.Footer().Element(ComposeFooter);
        }))
        .WithMetadata(new DocumentMetadata
        {
            Title = $"Rechnung {invoice.InvoiceNumber}",
            Author = "OrderSphere",
            Language = "de-DE",
        });
    }

    private static void ComposeHeader(IContainer container, Invoice invoice)
    {
        container.Column(col =>
        {
            col.Item().Height(3).Background(Accent);

            col.Item().PaddingTop(14).Row(row =>
            {
                // A drawn dot rather than "●": none of the embedded fonts carries that glyph.
                row.AutoItem().AlignMiddle().PaddingTop(3).Width(8).Height(8).CornerRadius(4).Background(Accent);
                row.ConstantItem(8);
                row.RelativeItem().AlignBottom().Text("OrderSphere").FontFamily(Display).FontSize(20);

                row.RelativeItem().AlignRight().AlignBottom().Column(c =>
                {
                    c.Item().AlignRight().Element(e => Eyebrow(e, "Rechnung"));
                    c.Item().AlignRight().Text(invoice.InvoiceNumber).FontFamily(Mono).FontSize(10);
                });
            });

            col.Item().PaddingTop(12).LineHorizontal(0.75f).LineColor(Ink);
        });
    }

    private static void ComposeContent(IContainer container, Invoice invoice)
    {
        container.Column(col =>
        {
            col.Item().Text("Rechnung").FontFamily(Display).FontSize(44).LineHeight(1);

            col.Item().PaddingTop(16).Row(row =>
            {
                row.RelativeItem(3).Column(c =>
                {
                    c.Item().Element(e => Eyebrow(e, "Rechnungsempfänger"));
                    c.Item().PaddingTop(4).Text(invoice.CustomerName).SemiBold().FontSize(11);
                    c.Item().Text(invoice.CustomerEmail).FontColor(Muted);
                });

                row.ConstantItem(24);

                row.RelativeItem(5).Column(c =>
                {
                    MetaLine(c, "Rechnungsnummer", invoice.InvoiceNumber);
                    MetaLine(c, "Rechnungsdatum", invoice.IssuedAt.ToString("dd.MM.yyyy", De));
                    MetaLine(c, "Bestellnummer", invoice.OrderId.ToString());
                });
            });

            col.Item().PaddingTop(28).Element(c => ComposeItems(c, invoice));

            col.Item().PaddingTop(12).AlignRight().Width(240).Element(c => ComposeTotals(
                c,
                ("Nettobetrag", invoice.NetAmount),
                ($"MwSt. {invoice.TaxRate.ToString("P0", De)}", invoice.TaxAmount),
                ("Gesamtbetrag", invoice.Total)));

            col.Item().PaddingTop(8).AlignRight().Text($"Alle Beträge in Euro, inklusive {invoice.TaxRate.ToString("P0", De)} Umsatzsteuer.")
                .FontSize(8).FontColor(Muted);

            if (invoice.Adjustments.Count > 0)
            {
                col.Item().PaddingTop(28).Element(c => ComposeAdjustments(c, invoice));
            }

        });
    }

    private static void ComposeItems(IContainer container, Invoice invoice)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(36);
                cols.RelativeColumn(6);
                cols.RelativeColumn(1.2f);
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("POS.");
                header.Cell().Element(HeaderCell).Text("ARTIKEL");
                header.Cell().Element(HeaderCell).AlignRight().Text("MENGE");
                header.Cell().Element(HeaderCell).AlignRight().Text("EINZELPREIS");
                header.Cell().Element(HeaderCell).AlignRight().Text("BETRAG");
            });

            var position = 0;
            foreach (var item in invoice.Items)
            {
                position++;
                var lineTotal = item.Quantity * item.UnitPrice;

                table.Cell().Element(BodyCell).Text(position.ToString("00", De)).FontFamily(Mono).FontColor(Muted);
                table.Cell().Element(BodyCell).Text(item.ProductName).SemiBold();
                table.Cell().Element(BodyCell).AlignRight().Text(item.Quantity.ToString(De)).FontFamily(Mono);
                table.Cell().Element(BodyCell).AlignRight().Text(Money(item.UnitPrice)).FontFamily(Mono);
                table.Cell().Element(BodyCell).AlignRight().Text(Money(lineTotal)).FontFamily(Mono);
            }
        });
    }

    private static void ComposeAdjustments(IContainer container, Invoice invoice)
    {
        container.Column(col =>
        {
            col.Item().Element(e => Eyebrow(e, "Nachträgliche Anpassungen"));
            col.Item().PaddingTop(2).Text("Rabatte und Gutschriften").FontFamily(Display).FontSize(20);

            col.Item().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1.6f);
                    cols.RelativeColumn(1.6f);
                    cols.RelativeColumn(5);
                    cols.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("DATUM");
                    header.Cell().Element(HeaderCell).Text("ART");
                    header.Cell().Element(HeaderCell).Text("BEGRÜNDUNG");
                    header.Cell().Element(HeaderCell).AlignRight().Text("NETTO");
                });

                foreach (var adjustment in invoice.Adjustments.OrderBy(a => a.AppliedAt))
                {
                    var typeLabel = adjustment.Type == InvoiceAdjustmentType.Discount ? "Rabatt" : "Gutschrift";

                    table.Cell().Element(BodyCell).Text(adjustment.AppliedAt.ToString("dd.MM.yyyy", De)).FontFamily(Mono);
                    table.Cell().Element(BodyCell).Text(typeLabel).SemiBold();
                    table.Cell().Element(BodyCell).Text(adjustment.Reason).FontColor(Muted);
                    table.Cell().Element(BodyCell).AlignRight().Text($"−{Money(adjustment.AmountNet)}").FontFamily(Mono).FontColor(AccentInk);
                }
            });

            col.Item().PaddingTop(12).AlignRight().Width(240).Element(c => ComposeTotals(
                c,
                ("Ursprünglicher Gesamtbetrag", invoice.Total),
                ("Angepasster Nettobetrag", invoice.AdjustedNet),
                ($"MwSt. {invoice.TaxRate.ToString("P0", De)}", invoice.AdjustedTax),
                ("Neuer Gesamtbetrag", invoice.AdjustedTotal)));
        });
    }

    // The last row is the grand total: set off by an ink rule and a larger mono figure on a sunk panel.
    // ShowEntire keeps the block on one page; a total split from its subtotals is unreadable.
    private static void ComposeTotals(IContainer container, params (string Label, decimal Amount)[] rows)
    {
        container.ShowEntire().Column(col =>
        {
            for (var i = 0; i < rows.Length - 1; i++)
            {
                var (label, amount) = rows[i];
                col.Item().BorderBottom(0.5f).BorderColor(Hairline).PaddingVertical(5).PaddingHorizontal(10).Row(row =>
                {
                    row.RelativeItem().Text(label).FontColor(Muted);
                    row.AutoItem().Text(Money(amount)).FontFamily(Mono);
                });
            }

            var (totalLabel, totalAmount) = rows[^1];
            col.Item().PaddingTop(6).BorderTop(1.25f).BorderColor(Ink).Background(Sunk)
                .PaddingVertical(9).PaddingHorizontal(10).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text(totalLabel).Bold().FontSize(10.5f);
                    row.AutoItem().AlignMiddle().Text(Money(totalAmount)).FontFamily(Mono).Medium().FontSize(14);
                });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(HairlineStrong).PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                text.Span("OrderSphere").FontFamily(Display).FontSize(10).FontColor(Ink);
                text.Span("   ·   support@ordersphere.dev");
            });

            row.AutoItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontFamily(Mono).FontSize(8).FontColor(Muted));
                text.Span("Seite ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private static void MetaLine(ColumnDescriptor col, string label, string value) =>
        col.Item().BorderBottom(0.5f).BorderColor(Hairline).PaddingVertical(4).Row(row =>
        {
            row.ConstantItem(88).AlignMiddle().Element(e => Eyebrow(e, label));
            row.RelativeItem().AlignMiddle().AlignRight().Text(value).FontFamily(Mono).FontSize(9);
        });

    // Mono, uppercase, tracked — the web's OsEyebrow.
    private static void Eyebrow(IContainer container, string text) =>
        container.Text(text.ToUpper(De)).FontFamily(Mono).FontSize(7).LetterSpacing(0.12f).FontColor(Muted).Medium();

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(0.75f).BorderColor(Ink).PaddingBottom(6).PaddingHorizontal(4)
            .DefaultTextStyle(x => x.FontFamily(Mono).FontSize(7).LetterSpacing(0.12f).FontColor(Muted).Medium());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Hairline).PaddingVertical(8).PaddingHorizontal(4);

    private static string Money(decimal amount) => amount.ToString("C", De);

    private static bool RegisterFonts()
    {
        var assembly = typeof(QuestPdfInvoiceService).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(FontResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFont(stream);
        }

        return true;
    }

    // Matches the LogicalName given to the embedded font files in the .csproj.
    private const string FontResourcePrefix = "InvoicingFonts.";
}
