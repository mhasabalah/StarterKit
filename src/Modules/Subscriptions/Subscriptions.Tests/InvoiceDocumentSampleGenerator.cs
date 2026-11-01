using FluentAssertions;
using QuestPDF.Fluent;
using Subscriptions.Domains.Subscriptions.Services;
using Xunit;

namespace Subscriptions.Tests;

/// <summary>
/// Renders real invoice PDFs through the production layout (<see cref="InvoiceDocument"/>)
/// so the output can be opened and visually verified. Opt-in: set the
/// GENERATE_INVOICE_PDF_SAMPLES=1 environment variable. Files are written to
/// INVOICE_PDF_OUTPUT_DIR (defaults to the system temp folder).
/// </summary>
public class InvoiceDocumentSampleGenerator
{
    public InvoiceDocumentSampleGenerator() => PdfConfiguration.Configure();

    [Fact]
    public void Generate_sample_invoice_pdfs()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_INVOICE_PDF_SAMPLES") != "1")
            return; // no-op during normal test runs

        var outDir = Environment.GetEnvironmentVariable("INVOICE_PDF_OUTPUT_DIR")
                     ?? Path.Combine(Path.GetTempPath(), "starterkit-invoice-samples");
        Directory.CreateDirectory(outDir);

        var happyPath = new InvoicePdfModel
        {
            InvoiceNumber = "INV-202606-00042",
            TypeLabel = "Activation",
            Description = "Subscription Activation — Pro Plan",
            SubscriberName = "Acme Ltd",
            Email = "billing@acme.example",
            Status = "Paid",
            Currency = "SAR",
            IssuedAt = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero),
            NetAmount = 1000m,
            TaxAmount = 150m,
            TotalAmount = 1150m,
            VatPercent = 15,
            IsDeferred = false
        };

        var arabic = happyPath with
        {
            InvoiceNumber = "INV-202606-00043",
            TypeLabel = "Renewal",
            Description = "تجديد الاشتراك — الباقة الاحترافية",
            SubscriberName = "شركة المثال المحدودة"
        };

        var happyPathFile = Path.Combine(outDir, "invoice-happy-path.pdf");
        var arabicFile = Path.Combine(outDir, "invoice-arabic.pdf");

        File.WriteAllBytes(happyPathFile, new InvoiceDocument(happyPath).GeneratePdf());
        File.WriteAllBytes(arabicFile, new InvoiceDocument(arabic).GeneratePdf());

        // Surfaced in test output so the paths are easy to find.
        Console.WriteLine($"SAMPLE_PDF::{happyPathFile}");
        Console.WriteLine($"SAMPLE_PDF::{arabicFile}");

        File.Exists(happyPathFile).Should().BeTrue();
        File.Exists(arabicFile).Should().BeTrue();
    }
}
