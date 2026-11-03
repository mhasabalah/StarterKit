using FluentAssertions;
using QuestPDF.Fluent;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;
using Xunit;

namespace Subscriptions.Tests;

public class InvoiceDocumentTests
{
    public InvoiceDocumentTests() => PdfConfiguration.Configure();

    private static InvoicePdfModel SampleModel(
        bool isCreditNote = false,
        string subscriberName = "Acme Ltd") => new()
    {
        InvoiceNumber = "INV-2026-000042",
        TypeLabel = "Activation",
        Description = "Subscription Activation — Pro Plan",
        SubscriberName = subscriberName,
        Email = "billing@acme.example",
        Status = "Paid",
        Currency = "SAR",
        IssuedAt = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero),
        NetAmount = 1000m,
        TaxAmount = 150m,
        TotalAmount = 1150m,
        TaxRate = 0.15m,
        IsCreditNote = isCreditNote
    };

    [Fact]
    public void GeneratePdf_produces_a_valid_pdf_document()
    {
        var bytes = new InvoiceDocument(SampleModel()).GeneratePdf();

        bytes.Should().NotBeNullOrEmpty();
        bytes.Length.Should().BeGreaterThan(1024);
        // PDF files begin with the "%PDF" magic header.
        bytes[..4].Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public void GeneratePdf_renders_credit_notes_without_throwing()
    {
        var model = SampleModel(isCreditNote: true) with { NetAmount = -1000m, TaxAmount = -150m, TotalAmount = -1150m };
        var act = () => new InvoiceDocument(model).GeneratePdf();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("0.15", "15%")]
    [InlineData("0.075", "7.5%")]
    [InlineData("0.05", "5%")]
    [InlineData("0", "0%")]
    public void Tax_rate_label_is_not_truncated(string rate, string expected)
    {
        var model = SampleModel() with { TaxRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) };

        model.TaxRateLabel.Should().Be(expected);
    }

    [Fact]
    public void GeneratePdf_renders_arabic_subscriber_name_without_throwing()
    {
        var act = () => new InvoiceDocument(SampleModel(subscriberName: "شركة المثال")).GeneratePdf();

        act.Should().NotThrow();
    }

    [Fact]
    public void GeneratePdf_renders_custom_seller_branding_and_billing_details_without_throwing()
    {
        var model = SampleModel() with
        {
            BillingAddress = "1 Example Street, Springfield",
            TaxId = "TAX-123",
            Seller = new InvoiceBrandingOptions
            {
                CompanyName = "Acme SaaS",
                Tagline = "Software for everyone",
                Website = null,
                SupportEmail = "billing@acme.example",
                Address = "42 Main Road",
                TaxId = "VAT-999"
            }
        };

        var bytes = new InvoiceDocument(model).GeneratePdf();

        bytes[..4].Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public void Default_seller_branding_is_generic()
    {
        var seller = new InvoiceBrandingOptions();

        seller.CompanyName.Should().Be("StarterKit");
        seller.SupportEmail.Should().Be("support@example.com");
        seller.NumberPrefix.Should().Be("INV");
    }
}
