using System.IO.Compression;
using System.Xml.Linq;

namespace DataTransfer.Tests.Engine;

/// <summary>Exports write real Excel numbers and dates (no "number stored as text"), and still re-import unchanged.</summary>
public sealed class TypedXlsxTests : IDisposable
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dt-typed-{Guid.NewGuid():N}.xlsx");

    private static readonly TransferColumnKind[] Kinds =
        [TransferColumnKind.Text, TransferColumnKind.Number, TransferColumnKind.Date, TransferColumnKind.Date, TransferColumnKind.Number];

    private async Task WriteAsync(params string?[][] rows)
    {
        await using var stream = File.Create(_path);
        await using var writer = TabularWriter.Create(stream, TransferFormat.Xlsx, Kinds);
        foreach (var row in rows)
            await writer.WriteRowAsync(row, CancellationToken.None);
        await writer.CompleteAsync(CancellationToken.None);
    }

    private XElement Sheet()
    {
        using var zip = ZipFile.OpenRead(_path);
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        return XDocument.Load(stream).Root!;
    }

    private static List<XElement> Cells(XElement sheet, int row) =>
        sheet.Descendants(Ns + "row").ElementAt(row - 1).Elements(Ns + "c").ToList();

    [Fact]
    public async Task Numbers_and_dates_become_typed_cells_and_text_stays_text()
    {
        await WriteAsync(
            ["Code", "Price", "Available", "Updated", "Stock"],
            ["007", "12.50", "2026-11-06", "2026-11-06T10:30:00", "n/a"]);

        var data = Cells(Sheet(), 2);
        data[0].Attribute("t")!.Value.Should().Be("inlineStr", "a code keeps its leading zeros");
        data[1].Attribute("t").Should().BeNull("a numeric cell");
        data[1].Element(Ns + "v")!.Value.Should().Be("12.50");
        data[2].Attribute("s")!.Value.Should().Be("2", "the date style (numFmt 14)");
        data[2].Element(Ns + "v")!.Value.Should().Be("46332", "2026-11-06 as an Excel serial");
        data[3].Attribute("s")!.Value.Should().Be("3", "the date-time style (numFmt 22)");
        double.Parse(data[3].Element(Ns + "v")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(46332.4375, 1e-9);
        data[4].Attribute("t")!.Value.Should().Be("inlineStr", "a Number column value that is not a number stays text");
    }

    [Fact]
    public async Task The_header_is_bold_frozen_and_sizes_the_columns()
    {
        await WriteAsync(["Code", "A rather long header name"], ["A", "1"]);

        var sheet = Sheet();
        Cells(sheet, 1).Should().OnlyContain(c => c.Attribute("s")!.Value == "1");
        var pane = sheet.Descendants(Ns + "pane").Single();
        pane.Attribute("state")!.Value.Should().Be("frozen");
        pane.Attribute("ySplit")!.Value.Should().Be("1");
        sheet.Descendants(Ns + "col").Select(c => c.Attribute("width")!.Value).Should().Equal("10", "29");
        sheet.Elements().Select(e => e.Name.LocalName).Should().ContainInOrder("sheetViews", "cols", "sheetData");
    }

    [Fact]
    public async Task A_typed_export_reads_back_as_the_same_invariant_text()
    {
        await WriteAsync(
            ["Code", "Price", "Available", "Updated", "Stock"],
            ["007", "12.5", "2026-11-06", null, "3"]);

        await using var reader = await TabularReader.OpenAsync(_path, TransferFormat.Xlsx, new DataTransferLimits(),
            CancellationToken.None);
        var rows = new List<TabularRow>();
        await foreach (var row in reader.ReadRowsAsync(0, CancellationToken.None))
            rows.Add(row);

        rows.Single().Cells.Take(3).Should().Equal("007", "12.5", "2026-11-06");
        CellText.Clean(rows.Single().Cells[3]).Should().BeNull();
        rows.Single().Cells[4].Should().Be("3");
    }

    [Theory]
    [InlineData("2026-11-06", 46332d, false)]
    [InlineData("1900-03-01", 61d, false)]
    [InlineData("2026-11-06T12:00:00", 46332.5d, true)]
    public void Iso_dates_map_to_excel_serials(string value, double serial, bool hasTime)
    {
        XlsxTabularWriter.TryExcelDate(value, out var actual, out var time).Should().BeTrue();
        actual.Should().BeApproximately(serial, 1e-9);
        time.Should().Be(hasTime);
    }

    [Theory]
    [InlineData("06/11/2026")]
    [InlineData("tomorrow")]
    [InlineData("2026-13-01")]
    public void Anything_but_an_iso_date_is_not_a_date(string value) =>
        XlsxTabularWriter.TryExcelDate(value, out _, out _).Should().BeFalse();

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
