using Catalog.Domains.Products.DataTransfer;
using DataTransfer.Contracts.Enums;

namespace Catalog.Tests.Features;

/// <summary>The catalog.products file contract: bilingual headers and hints, stable names, typed numbers.</summary>
public sealed class ProductTransferColumnsTests
{
    [Fact]
    public void Every_column_has_an_english_and_arabic_header_and_hint() =>
        ProductTransferColumns.Export.Should().OnlyContain(c =>
            !string.IsNullOrWhiteSpace(c.Header.En) && !string.IsNullOrWhiteSpace(c.Header.Ar) &&
            (c.Description == null || (!string.IsNullOrWhiteSpace(c.Description.En) && !string.IsNullOrWhiteSpace(c.Description.Ar))));

    [Fact]
    public void Column_names_are_unique_and_the_export_is_the_import_plus_status()
    {
        ProductTransferColumns.Export.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        ProductTransferColumns.Export.Take(ProductTransferColumns.Import.Count).Should().Equal(ProductTransferColumns.Import);
        ProductTransferColumns.Export[^1].Name.Should().Be(ProductTransferColumns.Status);
    }

    [Fact]
    public void Prices_are_numeric_columns_and_codes_stay_text()
    {
        ProductTransferColumns.Import.Where(c => c.Kind == TransferColumnKind.Number).Select(c => c.Name)
            .Should().BeEquivalentTo([ProductTransferColumns.Price, ProductTransferColumns.CompareAtPrice]);
        ProductTransferColumns.Import.Single(c => c.Name == ProductTransferColumns.Sku).Kind.Should().Be(TransferColumnKind.Text);
    }
}
