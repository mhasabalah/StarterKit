using System.Globalization;
using static DataTransfer.Tests.Engine.ExcelAuthoredWorkbook;

namespace DataTransfer.Tests.Engine;

/// <summary>Real-world input: what users upload is written by Excel, not by our writer.</summary>
public sealed class ExcelAuthoredWorkbookTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dt-excel-{Guid.NewGuid():N}.xlsx");

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ar-EG")]
    public async Task Shared_strings_numbers_long_numeric_codes_and_dates_read_as_invariant_text(string serverCulture)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(serverCulture);
        try
        {
            await AssertReadsInvariantAsync();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    private async Task AssertReadsInvariantAsync()
    {
        Create(_path,
        [
            [new Text("sku"), new Text("name"), new Text("price"), new Text("barcode"), new Text("available")],
            [new Text("A-1"), new Text("قميص"), new Number(12.5), new Number(1234567890123456), new Date(new DateOnly(2026, 11, 6))],
            [],
            [new Text("A-2"), new Text("Shirt"), new Number(199), new Number(42), null]
        ]);

        await using var reader = await TabularReader.OpenAsync(_path, TransferFormat.Xlsx, new DataTransferLimits(),
            CancellationToken.None);
        var rows = new List<TabularRow>();
        await foreach (var row in reader.ReadRowsAsync(0, CancellationToken.None))
            rows.Add(row);

        reader.Header.Should().Equal("sku", "name", "price", "barcode", "available");
        reader.EstimatedRows.Should().Be(3, "the sheet dimension A1:E4 announces 3 data rows (the empty one included)");
        rows.Select(r => r.RowNumber).Should().Equal(2, 4);
        rows[0].Cells.Should().Equal("A-1", "قميص", "12.5", "1234567890123456", "2026-11-06");
        rows[1].Cells.Take(4).Should().Equal("A-2", "Shirt", "199", "42");
        CellText.Clean(rows[1].Cells.ElementAtOrDefault(4)).Should().BeNull();
    }

    [Fact]
    public async Task Only_the_first_worksheet_is_read()
    {
        Create(_path, [[new Text("sku")], [new Text("A")]]);

        await using var reader = await TabularReader.OpenAsync(_path, TransferFormat.Xlsx, new DataTransferLimits(),
            CancellationToken.None);
        var rows = new List<TabularRow>();
        await foreach (var row in reader.ReadRowsAsync(0, CancellationToken.None))
            rows.Add(row);

        rows.Should().ContainSingle().Which.Cells.Should().Equal("A");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
