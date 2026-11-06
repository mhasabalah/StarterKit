using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DataTransfer.Engine;

/// <summary>
/// Streaming CSV/XLSX writer (own implementation: rows go straight to the output, nothing is buffered, no DOM). The
/// first row is the header. CSV cells are text. XLSX cells follow the column kinds: numbers and dates become real Excel
/// cells, everything else stays text, so an export re-imports unchanged either way.
/// </summary>
public abstract class TabularWriter : IAsyncDisposable
{
    /// <summary>Data rows an .xlsx worksheet can hold (1,048,576 minus the header).</summary>
    public const long MaxXlsxRows = 1_048_575;

    public abstract Task WriteRowAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken);

    /// <summary>Flushes and closes the file structure (call once, before disposing).</summary>
    public abstract Task CompleteAsync(CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();

    /// <param name="kinds">Per column (XLSX only); missing entries are <see cref="TransferColumnKind.Text"/>.</param>
    /// <param name="culture">Reader's language (XLSX only): Arabic sheets are right-to-left with an Arabic sheet name.</param>
    public static TabularWriter Create(Stream output, TransferFormat format, IReadOnlyList<TransferColumnKind>? kinds = null,
        string culture = TransferCulture.English) =>
        format == TransferFormat.Xlsx ? new XlsxTabularWriter(output, kinds ?? [], culture) : new CsvTabularWriter(output);

    public static string ContentType(TransferFormat format) => format == TransferFormat.Xlsx
        ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        : "text/csv";

    public static string Extension(TransferFormat format) => format == TransferFormat.Xlsx ? ".xlsx" : ".csv";
}

/// <summary>RFC 4180 CSV, UTF-8 with BOM (Excel then shows Arabic correctly), CRLF, formula-injection guard.</summary>
public sealed class CsvTabularWriter : TabularWriter
{
    private readonly StreamWriter _writer;

    public CsvTabularWriter(Stream output) =>
        _writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true)
        {
            NewLine = "\r\n"
        };

    public override async Task WriteRowAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
                await _writer.WriteAsync(',');
            await _writer.WriteAsync(Quote(CellText.EscapeFormula(cells[i])));
        }

        await _writer.WriteLineAsync();
    }

    public override Task CompleteAsync(CancellationToken cancellationToken) => _writer.FlushAsync(cancellationToken);

    public override ValueTask DisposeAsync() => _writer.DisposeAsync();

    public static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.AsSpan().IndexOfAny(",\"\r\n;") < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

/// <summary>
/// Minimal SpreadsheetML workbook with one sheet, written entry by entry into a <see cref="ZipArchive"/> (static parts
/// first, then the sheet streamed row by row):
/// <list type="bullet">
/// <item>row 1 is the header: bold, frozen (stays visible while scrolling), column widths from the header text;</item>
/// <item><see cref="TransferColumnKind.Number"/> cells with an invariant number become numeric cells and
/// <see cref="TransferColumnKind.Date"/> cells with an ISO date become Excel dates. A value that does not parse stays
/// text, so nothing is ever lost or reformatted;</item>
/// <item>everything else is an inline string (never a formula);</item>
/// <item>for an Arabic reader the sheet is right-to-left and named in Arabic.</item>
/// </list>
/// </summary>
public sealed class XlsxTabularWriter : TabularWriter
{
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // cellXfs indexes in styles.xml
    private const string HeaderStyle = "1";
    private const string DateStyle = "2";
    private const string DateTimeStyle = "3";

    private static readonly DateTime ExcelEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly ZipArchive _zip;
    private readonly IReadOnlyList<TransferColumnKind> _kinds;
    private readonly bool _rightToLeft;
    private Stream? _sheetStream;
    private XmlWriter? _sheet;
    private long _row;

    public XlsxTabularWriter(Stream output, IReadOnlyList<TransferColumnKind>? kinds = null,
        string culture = TransferCulture.English)
    {
        _zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        _kinds = kinds ?? [];
        _rightToLeft = TransferCulture.IsArabic(culture);
        WriteStaticParts(TransferText.For(culture, "Transfer.Sheet.name"));
    }

    public override async Task WriteRowAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken)
    {
        var sheet = await EnsureSheetAsync(cells);
        _row++;
        await sheet.WriteStartElementAsync(null, "row", SheetNs);
        await sheet.WriteAttributeStringAsync(null, "r", null, _row.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < cells.Count; i++)
        {
            if (_row == 1)
                await WriteTextAsync(sheet, cells[i], HeaderStyle);
            else
                await WriteCellAsync(sheet, KindOf(i), cells[i]);
        }

        await sheet.WriteEndElementAsync();
    }

    public override async Task CompleteAsync(CancellationToken cancellationToken)
    {
        var sheet = await EnsureSheetAsync([]);
        await sheet.WriteEndElementAsync(); // sheetData
        await sheet.WriteEndElementAsync(); // worksheet
        await sheet.WriteEndDocumentAsync();
        await sheet.FlushAsync();
        sheet.Dispose();
        _sheet = null;
        await _sheetStream!.DisposeAsync();
        _sheetStream = null;
    }

    public override async ValueTask DisposeAsync()
    {
        _sheet?.Dispose();
        if (_sheetStream is not null)
            await _sheetStream.DisposeAsync();
        _zip.Dispose();
    }

    /// <summary>The Excel serial of an ISO date (<c>yyyy-MM-dd</c>) or date-time, and whether it has a time part.</summary>
    public static bool TryExcelDate(string value, out double serial, out bool hasTime)
    {
        serial = 0;
        hasTime = false;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            serial = date.DayNumber - DateOnly.FromDateTime(ExcelEpoch).DayNumber;
            return true;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime) ||
            !value.Contains('T', StringComparison.Ordinal))
        {
            return false;
        }

        serial = (DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified) - ExcelEpoch).TotalDays;
        hasTime = true;
        return true;
    }

    private TransferColumnKind KindOf(int column) => column < _kinds.Count ? _kinds[column] : TransferColumnKind.Text;

    private static Task WriteCellAsync(XmlWriter sheet, TransferColumnKind kind, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return WriteTextAsync(sheet, value, style: null);

        if (kind == TransferColumnKind.Number &&
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return WriteNumberAsync(sheet, number.ToString(CultureInfo.InvariantCulture), style: null);
        }

        if (kind == TransferColumnKind.Date && TryExcelDate(value, out var serial, out var hasTime))
        {
            return WriteNumberAsync(sheet, serial.ToString("R", CultureInfo.InvariantCulture),
                hasTime ? DateTimeStyle : DateStyle);
        }

        return WriteTextAsync(sheet, value, style: null);
    }

    private static async Task WriteNumberAsync(XmlWriter sheet, string value, string? style)
    {
        await sheet.WriteStartElementAsync(null, "c", SheetNs);
        if (style is not null)
            await sheet.WriteAttributeStringAsync(null, "s", null, style);
        await sheet.WriteElementStringAsync(null, "v", SheetNs, value);
        await sheet.WriteEndElementAsync();
    }

    private static async Task WriteTextAsync(XmlWriter sheet, string? value, string? style)
    {
        await sheet.WriteStartElementAsync(null, "c", SheetNs);
        await sheet.WriteAttributeStringAsync(null, "t", null, "inlineStr");
        if (style is not null)
            await sheet.WriteAttributeStringAsync(null, "s", null, style);
        await sheet.WriteStartElementAsync(null, "is", SheetNs);
        await sheet.WriteStartElementAsync(null, "t", SheetNs);
        await sheet.WriteAttributeStringAsync("xml", "space", null, "preserve");
        await sheet.WriteStringAsync(CellText.ToXmlSafe(value ?? string.Empty));
        await sheet.WriteEndElementAsync();
        await sheet.WriteEndElementAsync();
        await sheet.WriteEndElementAsync();
    }

    /// <summary>Starts the sheet: frozen header pane and column widths (from the header) come before the data.</summary>
    private async Task<XmlWriter> EnsureSheetAsync(IReadOnlyList<string?> header)
    {
        if (_sheet is not null)
            return _sheet;

        _sheetStream = _zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
        _sheet = XmlWriter.Create(_sheetStream, new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false) });
        await _sheet.WriteStartDocumentAsync(true);
        await _sheet.WriteStartElementAsync(null, "worksheet", SheetNs);
        if (header.Count > 0)
        {
            await WriteFrozenHeaderAsync(_sheet, _rightToLeft);
            await WriteColumnWidthsAsync(_sheet, header);
        }

        await _sheet.WriteStartElementAsync(null, "sheetData", SheetNs);
        return _sheet;
    }

    private static async Task WriteFrozenHeaderAsync(XmlWriter sheet, bool rightToLeft)
    {
        await sheet.WriteStartElementAsync(null, "sheetViews", SheetNs);
        await sheet.WriteStartElementAsync(null, "sheetView", SheetNs);
        if (rightToLeft)
            await sheet.WriteAttributeStringAsync(null, "rightToLeft", null, "1");
        await sheet.WriteAttributeStringAsync(null, "workbookViewId", null, "0");
        await sheet.WriteStartElementAsync(null, "pane", SheetNs);
        await sheet.WriteAttributeStringAsync(null, "ySplit", null, "1");
        await sheet.WriteAttributeStringAsync(null, "topLeftCell", null, "A2");
        await sheet.WriteAttributeStringAsync(null, "activePane", null, "bottomLeft");
        await sheet.WriteAttributeStringAsync(null, "state", null, "frozen");
        await sheet.WriteEndElementAsync();
        await sheet.WriteEndElementAsync();
        await sheet.WriteEndElementAsync();
    }

    private async Task WriteColumnWidthsAsync(XmlWriter sheet, IReadOnlyList<string?> header)
    {
        await sheet.WriteStartElementAsync(null, "cols", SheetNs);
        for (var i = 0; i < header.Count; i++)
        {
            var width = KindOf(i) == TransferColumnKind.Date
                ? 14
                : Math.Clamp((header[i]?.Length ?? 0) + 4, 10, 50);
            var column = (i + 1).ToString(CultureInfo.InvariantCulture);
            await sheet.WriteStartElementAsync(null, "col", SheetNs);
            await sheet.WriteAttributeStringAsync(null, "min", null, column);
            await sheet.WriteAttributeStringAsync(null, "max", null, column);
            await sheet.WriteAttributeStringAsync(null, "width", null, width.ToString(CultureInfo.InvariantCulture));
            await sheet.WriteAttributeStringAsync(null, "customWidth", null, "1");
            await sheet.WriteEndElementAsync();
        }

        await sheet.WriteEndElementAsync();
    }

    private void WriteStaticParts(string sheetName)
    {
        Part("[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
        Part("_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Part("xl/workbook.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{{SHEET}}" sheetId="1" r:id="rId1"/></sheets></workbook>""".Replace("{{SHEET}}", sheetName, StringComparison.Ordinal));
        Part("xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
        // cellXfs: 0 default, 1 bold header, 2 date (numFmt 14), 3 date-time (numFmt 22).
        Part("xl/styles.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="22" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
    }

    private void Part(string name, string xml)
    {
        using var stream = _zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
    }
}
