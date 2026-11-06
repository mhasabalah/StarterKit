using System.Globalization;
using System.Data.Common;
using System.IO.Compression;
using System.Text;
using Sylvan.Data.Csv;
using Sylvan.Data.Excel;

namespace DataTransfer.Engine;

/// <summary>One data row: its row number in the file (header = 1) and its raw cells.</summary>
public sealed record TabularRow(long RowNumber, string?[] Cells);

/// <summary>
/// Streaming reader over a local copy of an uploaded CSV or XLSX file (Sylvan; memory stays flat for any size).
/// The first row is the header; fully empty rows are skipped but still counted, so row numbers match what the user
/// sees in a spreadsheet. Limits are enforced while reading.
/// </summary>
public sealed class TabularReader : IAsyncDisposable
{
    private static readonly char[] Delimiters = [',', ';', '\t', '|'];

    private readonly DbDataReader _reader;
    private readonly DataTransferLimits _limits;
    private long _rowNumber;

    private TabularReader(DbDataReader reader, IReadOnlyList<string?> header, DataTransferLimits limits)
    {
        _reader = reader;
        Header = header;
        _limits = limits;
        _rowNumber = 1;
    }

    public IReadOnlyList<string?> Header { get; }

    /// <summary>
    /// Data rows announced by the file without reading it (XLSX sheet dimension, header excluded; empty rows included),
    /// or null (CSV). For progress only: the exact count is known when the job finishes.
    /// </summary>
    public long? EstimatedRows => _reader is ExcelDataReader { RowCount: > 1 } excel ? excel.RowCount - 1 : null;

    /// <summary>Opens the file and reads its header row. Throws <see cref="InvalidTransferFileException"/>.</summary>
    public static async Task<TabularReader> OpenAsync(string path, TransferFormat format, DataTransferLimits limits,
        CancellationToken cancellationToken)
    {
        var reader = format == TransferFormat.Xlsx
            ? await OpenXlsxAsync(path, limits, cancellationToken)
            : await OpenCsvAsync(path, cancellationToken);

        try
        {
            if (!await ReadNextAsync(reader, cancellationToken))
                throw new InvalidTransferFileException("empty_file");

            var header = ReadCells(reader, limits.MaxColumns + 1);
            if (header.Length > limits.MaxColumns)
            {
                throw new InvalidTransferFileException("too_many_columns", limits.MaxColumns);
            }

            return new TabularReader(reader, header, limits);
        }
        catch
        {
            await reader.DisposeAsync();
            throw;
        }
    }

    /// <summary>Data rows after the header, skipping rows up to and including <paramref name="afterRow"/> (resume).</summary>
    public async IAsyncEnumerable<TabularRow> ReadRowsAsync(long afterRow,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long dataRows = 0;
        while (await ReadNextAsync(_reader, cancellationToken))
        {
            _rowNumber++;
            var cells = ReadCells(_reader, Header.Count);
            if (Array.TrueForAll(cells, string.IsNullOrWhiteSpace))
                continue;

            if (++dataRows > _limits.MaxRows)
                throw new InvalidTransferFileException("too_many_rows", _limits.MaxRows);

            if (_rowNumber > afterRow)
                yield return new TabularRow(_rowNumber, cells);
        }
    }

    public ValueTask DisposeAsync() => _reader.DisposeAsync();

    /// <summary>
    /// The next record. A CSV syntax error (e.g. an unclosed quote) ANYWHERE in the file fails it as
    /// <c>invalid_csv</c> with the row, like any other file problem: never retried, never a generic failure.
    /// </summary>
    private static async Task<bool> ReadNextAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken);
        }
        catch (CsvFormatException ex)
        {
            throw new InvalidTransferFileException("invalid_csv", ex.RowNumber + 1);
        }
    }

    private static string?[] ReadCells(DbDataReader reader, int maxCells)
    {
        var count = Math.Min(FieldCountOf(reader), maxCells);
        var cells = new string?[count];
        for (var i = 0; i < count; i++)
            cells[i] = reader.IsDBNull(i) ? null : reader.GetString(i);
        return cells;
    }

    private static int FieldCountOf(DbDataReader reader) => reader switch
    {
        ExcelDataReader excel => excel.RowFieldCount,
        CsvDataReader csv => csv.RowFieldCount,
        _ => reader.FieldCount
    };

    private static async Task<DbDataReader> OpenCsvAsync(string path, CancellationToken cancellationToken)
    {
        var delimiter = await SniffDelimiterAsync(path, cancellationToken);
        var text = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var options = new CsvDataReaderOptions
        {
            HasHeaders = false,
            Delimiter = delimiter,
            MaxBufferSize = 4 * 1024 * 1024,
            OwnsReader = true,
            Culture = CultureInfo.InvariantCulture
        };

        try
        {
            return await CsvDataReader.CreateAsync(text, options, cancellationToken);
        }
        catch (CsvFormatException ex)
        {
            text.Dispose();
            throw new InvalidTransferFileException("invalid_csv", ex.RowNumber + 1);
        }
    }

    private static async Task<DbDataReader> OpenXlsxAsync(string path, DataTransferLimits limits,
        CancellationToken cancellationToken)
    {
        EnsureSafeWorkbook(path, limits);
        // Invariant culture: numbers and dates become "12.5" / "2026-11-06" whatever the server's culture.
        var options = new ExcelDataReaderOptions
        {
            Schema = ExcelSchema.NoHeaders,
            FormulaErrorHandling = FormulaErrorHandling.Null,
            Culture = CultureInfo.InvariantCulture
        };
        try
        {
            return await ExcelDataReader.CreateAsync(path, options, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or IOException)
        {
            throw new InvalidTransferFileException("invalid_xlsx");
        }
    }

    /// <summary>Zip-bomb guard: the declared uncompressed size and entry count, before any XML is parsed.</summary>
    public static void EnsureSafeWorkbook(string path, DataTransferLimits limits)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > limits.MaxZipEntries)
                throw new InvalidTransferFileException("invalid_xlsx");

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > limits.MaxUncompressedBytes)
                    throw new InvalidTransferFileException("file_too_large", "Transfer.File.uncompressed_too_large", []);
            }
        }
        catch (InvalidDataException)
        {
            throw new InvalidTransferFileException("invalid_xlsx");
        }
    }

    /// <summary>The most frequent of <c>, ; TAB |</c> in the first line, outside quotes (Excel saves ';' in many locales).</summary>
    public static async Task<char> SniffDelimiterAsync(string path, CancellationToken cancellationToken)
    {
        using var text = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var line = await text.ReadLineAsync(cancellationToken) ?? string.Empty;

        var counts = new int[Delimiters.Length];
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (!quoted && Array.IndexOf(Delimiters, ch) is var index and >= 0)
                counts[index]++;
        }

        var best = 0;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[best])
                best = i;
        }

        return Delimiters[best];
    }
}
