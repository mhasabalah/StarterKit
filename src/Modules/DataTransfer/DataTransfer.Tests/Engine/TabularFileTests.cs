using System.IO.Compression;
using System.Text;

namespace DataTransfer.Tests.Engine;

/// <summary>Real files on disk: our streaming writers produce what the Sylvan readers (and Excel) read back.</summary>
public sealed class TabularFileTests : IAsyncLifetime
{
    private readonly List<string> _files = [];
    private static readonly DataTransferLimits Limits = new();

    [Theory]
    [InlineData(TransferFormat.Csv)]
    [InlineData(TransferFormat.Xlsx)]
    public async Task Written_file_reads_back_identically_including_arabic_quotes_newlines_and_empty_cells(TransferFormat format)
    {
        string?[][] rows =
        [
            ["sku", "name", "note"],
            ["A-1", "قميص أحمر", "has, comma"],
            ["A-2", "say \"hi\"", "line1\nline2"],
            ["A-3", "x", null]
        ];
        var path = await WriteAsync(format, rows);

        await using var reader = await TabularReader.OpenAsync(path, format, Limits, CancellationToken.None);
        var read = await ReadAllAsync(reader);

        reader.Header.Should().Equal("sku", "name", "note");
        read.Select(r => r.RowNumber).Should().Equal(2, 3, 4);
        read[0].Cells.Should().Equal("A-1", "قميص أحمر", "has, comma");
        read[1].Cells.Should().Equal("A-2", "say \"hi\"", "line1\nline2");
        CellText.Clean(read[2].Cells.ElementAtOrDefault(2)).Should().BeNull();
    }

    [Fact]
    public async Task Csv_export_starts_with_a_utf8_bom_and_guards_formulas()
    {
        var path = await WriteAsync(TransferFormat.Csv, [["a"], ["=1+1"]]);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(bytes).Should().Contain("'=1+1");
    }

    [Fact]
    public async Task Semicolon_csv_from_european_excel_is_detected()
    {
        var path = Track(".csv");
        await File.WriteAllTextAsync(path, "sku;name\nA;\"x;y\"\n");

        (await TabularReader.SniffDelimiterAsync(path, CancellationToken.None)).Should().Be(';');
        await using var reader = await TabularReader.OpenAsync(path, TransferFormat.Csv, Limits, CancellationToken.None);
        var rows = await ReadAllAsync(reader);
        rows.Single().Cells.Should().Equal("A", "x;y");
    }

    [Fact]
    public async Task Empty_rows_are_skipped_but_counted_so_row_numbers_match_the_spreadsheet()
    {
        var path = Track(".csv");
        await File.WriteAllTextAsync(path, "sku\nA\n\n,\nB\n");

        await using var reader = await TabularReader.OpenAsync(path, TransferFormat.Csv, Limits, CancellationToken.None);
        var rows = await ReadAllAsync(reader);

        rows.Select(r => (r.RowNumber, r.Cells[0])).Should().Equal((2L, "A"), (5L, "B"));
    }

    [Fact]
    public async Task Resume_skips_rows_up_to_the_checkpoint()
    {
        var path = await WriteAsync(TransferFormat.Csv, [["sku"], ["A"], ["B"], ["C"]]);

        await using var reader = await TabularReader.OpenAsync(path, TransferFormat.Csv, Limits, CancellationToken.None);
        var rows = new List<TabularRow>();
        await foreach (var row in reader.ReadRowsAsync(afterRow: 3, CancellationToken.None))
            rows.Add(row);

        rows.Should().ContainSingle().Which.Cells[0].Should().Be("C");
    }

    [Fact]
    public async Task More_rows_than_the_limit_fail_with_too_many_rows()
    {
        var path = await WriteAsync(TransferFormat.Csv, [["sku"], ["A"], ["B"], ["C"]]);

        await using var reader = await TabularReader.OpenAsync(path, TransferFormat.Csv, new DataTransferLimits { MaxRows = 2 },
            CancellationToken.None);
        var act = () => ReadAllAsync(reader);

        (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which.ErrorCode.Should().Be("too_many_rows");
    }

    [Fact]
    public async Task More_columns_than_the_limit_fail_with_too_many_columns()
    {
        var path = await WriteAsync(TransferFormat.Csv, [["a", "b", "c"]]);

        var act = () => TabularReader.OpenAsync(path, TransferFormat.Csv, new DataTransferLimits { MaxColumns = 2 },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which.ErrorCode.Should().Be("too_many_columns");
    }

    [Fact]
    public async Task Empty_file_fails_with_empty_file()
    {
        var path = Track(".csv");
        await File.WriteAllTextAsync(path, string.Empty);

        var act = () => TabularReader.OpenAsync(path, TransferFormat.Csv, Limits, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which.ErrorCode.Should().Be("empty_file");
    }

    [Fact]
    public async Task Workbook_over_the_uncompressed_limit_is_refused_before_parsing()
    {
        var path = await WriteAsync(TransferFormat.Xlsx, [["sku"], [new string('x', 50_000)]]);

        var act = () => TabularReader.OpenAsync(path, TransferFormat.Xlsx,
            new DataTransferLimits { MaxUncompressedBytes = 10_000 }, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which.ErrorCode.Should().Be("file_too_large");
    }

    [Fact]
    public async Task Workbook_with_too_many_zip_entries_is_refused()
    {
        var path = Track(".xlsx");
        await using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (var i = 0; i < 20; i++)
                zip.CreateEntry($"junk/{i}.xml");
        }

        var act = () => TabularReader.OpenAsync(path, TransferFormat.Xlsx, new DataTransferLimits { MaxZipEntries = 10 },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which.ErrorCode.Should().Be("invalid_xlsx");
    }

    [Fact]
    public async Task A_csv_renamed_to_xlsx_fails_with_invalid_xlsx()
    {
        var path = Track(".xlsx");
        await File.WriteAllTextAsync(path, "sku\nA\n");

        var act = () => TabularReader.OpenAsync(path, TransferFormat.Xlsx, Limits, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which.ErrorCode.Should().Be("invalid_xlsx");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var file in _files.Where(File.Exists))
            File.Delete(file);
        return Task.CompletedTask;
    }

    private string Track(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dt-test-{Guid.NewGuid():N}{extension}");
        _files.Add(path);
        return path;
    }

    private async Task<string> WriteAsync(TransferFormat format, string?[][] rows)
    {
        var path = Track(TabularWriter.Extension(format));
        await using var stream = File.Create(path);
        await using var writer = TabularWriter.Create(stream, format);
        foreach (var row in rows)
            await writer.WriteRowAsync(row, CancellationToken.None);
        await writer.CompleteAsync(CancellationToken.None);
        return path;
    }

    private static async Task<List<TabularRow>> ReadAllAsync(TabularReader reader)
    {
        var rows = new List<TabularRow>();
        await foreach (var row in reader.ReadRowsAsync(0, CancellationToken.None))
            rows.Add(row);
        return rows;
    }
}
