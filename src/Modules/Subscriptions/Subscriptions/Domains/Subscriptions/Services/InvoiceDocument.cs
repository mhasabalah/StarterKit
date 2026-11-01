using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// QuestPDF layout for a subscription invoice. Pure presentation:
/// it depends only on <see cref="InvoicePdfModel"/>, never on EF entities,
/// so it can be rendered and asserted on in unit tests.
/// </summary>
public sealed class InvoiceDocument(InvoicePdfModel model) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(50, QuestPDF.Infrastructure.Unit.Point);
            page.DefaultTextStyle(x => x
                .FontSize(11)
                .FontFamily(Fonts.Lato)
                .Fallback(f => f.FontFamily(PdfConfiguration.ArabicFontFamily)));

            page.Content().Column(column =>
            {
                column.Spacing(16);

                // ── Header ──────────────────────────────────────────────
                column.Item()
                    .Background("#0f172a")
                    .Padding(16)
                    .Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(model.Seller.CompanyName)
                                .Bold().FontSize(22).FontColor("#ffffff");
                            if (!string.IsNullOrWhiteSpace(model.Seller.Tagline))
                                col.Item().Text(model.Seller.Tagline)
                                    .FontSize(11).FontColor("#94a3b8");
                            if (!string.IsNullOrWhiteSpace(model.Seller.Address))
                                col.Item().Text(model.Seller.Address)
                                    .FontSize(9).FontColor("#94a3b8");
                            if (!string.IsNullOrWhiteSpace(model.Seller.TaxId))
                                col.Item().Text($"Tax ID: {model.Seller.TaxId}")
                                    .FontSize(9).FontColor("#94a3b8");
                        });
                        row.RelativeItem().AlignRight().Column(col =>
                        {
                            col.Item().Text("INVOICE")
                                .Bold().FontSize(18).FontColor("#ffffff");
                            col.Item().Text(model.InvoiceNumber)
                                .FontSize(12).FontColor("#94a3b8");
                        });
                    });

                // ── Info block ───────────────────────────────────────────
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Issued To").Bold().FontSize(12).FontColor("#0f172a");
                        col.Item().PaddingTop(4).Text(model.SubscriberName).FontSize(11);
                        col.Item().Text(model.Email).FontSize(11).FontColor("#64748b");
                        if (!string.IsNullOrWhiteSpace(model.BillingAddress))
                            col.Item().Text(model.BillingAddress).FontSize(11).FontColor("#64748b");
                        if (!string.IsNullOrWhiteSpace(model.TaxId))
                            col.Item().Text($"Tax ID: {model.TaxId}").FontSize(11).FontColor("#64748b");
                    });
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Invoice Details").Bold().FontSize(12).FontColor("#0f172a");
                        col.Item().PaddingTop(4).Text($"Number: {model.InvoiceNumber}").FontSize(11).FontColor("#64748b");
                        col.Item().Text($"Type: {model.TypeLabel}").FontSize(11).FontColor("#64748b");
                        col.Item().Text($"Date: {model.IssuedAt:MMMM dd, yyyy}").FontSize(11).FontColor("#64748b");
                        col.Item().Text($"Status: {model.Status}").FontSize(11).FontColor("#64748b");
                    });
                });

                column.Item().LineHorizontal(1).LineColor("#e2e8f0");

                // ── Line items ───────────────────────────────────────────
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(5);
                        cols.RelativeColumn(1);
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        static IContainer HeaderCell(IContainer c) =>
                            c.Background("#f1f5f9").Padding(8);

                        header.Cell().Element(HeaderCell).Text("Description").Bold().FontColor("#0f172a");
                        header.Cell().Element(HeaderCell).AlignCenter().Text("Qty").Bold().FontColor("#0f172a");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Unit Price").Bold().FontColor("#0f172a");
                        header.Cell().Element(HeaderCell).AlignRight().Text("Amount").Bold().FontColor("#0f172a");
                    });

                    table.Cell().Padding(8).Text(model.Description);
                    table.Cell().Padding(8).AlignCenter().Text("1");
                    table.Cell().Padding(8).AlignRight().Text($"{model.NetAmount:F2} {model.Currency}");
                    table.Cell().Padding(8).AlignRight().Text($"{model.NetAmount:F2} {model.Currency}");
                });

                column.Item().LineHorizontal(1).LineColor("#e2e8f0");

                // ── Summary ──────────────────────────────────────────────
                column.Item().AlignRight().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                    });

                    table.Cell().Padding(4).Text("Subtotal (excl. VAT)");
                    table.Cell().Padding(4).AlignRight().Text($"{model.NetAmount:F2} {model.Currency}");

                    table.Cell().Padding(4).Text($"VAT ({model.VatPercent}%)").FontColor("#64748b");
                    table.Cell().Padding(4).AlignRight().Text($"{model.TaxAmount:F2} {model.Currency}").FontColor("#64748b");

                    table.Cell().Padding(4).Text("Total").Bold().FontSize(14).FontColor("#0f172a");
                    table.Cell().Padding(4).AlignRight().Text($"{model.TotalAmount:F2} {model.Currency}").Bold().FontSize(14).FontColor("#0f172a");
                });

                if (model.IsDeferred)
                {
                    column.Item().AlignRight()
                        .Text("* Deferred — will be collected at next renewal")
                        .Italic().FontSize(9).FontColor("#94a3b8");
                }

                column.Item().LineHorizontal(1).LineColor("#e2e8f0");

                // ── Footer ───────────────────────────────────────────────
                column.Item().AlignCenter().Column(col =>
                {
                    var contact = string.Join("  ·  ",
                        new[] { model.Seller.Website, model.Seller.SupportEmail }
                            .Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (contact.Length > 0)
                        col.Item().Text(contact).FontSize(9).FontColor("#94a3b8");
                    col.Item().Text($"Thank you for choosing {model.Seller.CompanyName}")
                        .FontSize(9).FontColor("#94a3b8");
                });
            });
        });
    }
}
