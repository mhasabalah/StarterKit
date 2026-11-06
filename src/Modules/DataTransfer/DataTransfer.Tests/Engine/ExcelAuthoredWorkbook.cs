using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace DataTransfer.Tests.Engine;

/// <summary>
/// Writes a workbook the way Excel does (NOT our inline-string writer): a shared-strings table, a styles part with a
/// date number format, typed numeric cells, a date serial, a sheet dimension and a second worksheet. Used to prove
/// the reader handles real user files. Cell values: <see cref="Text"/>, <see cref="Number"/>, <see cref="Date"/> or null.
/// </summary>
public static class ExcelAuthoredWorkbook
{
    public abstract record Cell;

    public sealed record Text(string Value) : Cell;

    public sealed record Number(double Value) : Cell;

    public sealed record Date(DateOnly Value) : Cell;

    public static void Create(string path, IReadOnlyList<IReadOnlyList<Cell?>> rows)
    {
        var strings = new List<string>();
        var sheet = new StringBuilder();
        var maxCols = rows.Max(r => r.Count);
        sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sheet.Append(CultureInfo.InvariantCulture, $"""<dimension ref="A1:{Column(maxCols - 1)}{rows.Count}"/><sheetData>""");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append(CultureInfo.InvariantCulture, $"""<row r="{r + 1}">""");
            for (var c = 0; c < rows[r].Count; c++)
                AppendCell(sheet, $"{Column(c)}{r + 1}", rows[r][c], strings);
            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Part(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
        Part(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Part(zip, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Products" sheetId="1" r:id="rId1"/><sheet name="Notes" sheetId="2" r:id="rId2"/></sheets></workbook>""");
        Part(zip, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
        Part(zip, "xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>""");
        Part(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        Part(zip, "xl/worksheets/sheet2.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>ignored sheet</t></is></c></row></sheetData></worksheet>""");
        Part(zip, "xl/sharedStrings.xml",
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{strings.Count}" uniqueCount="{strings.Count}">""" +
            string.Concat(strings.Select(t => $"<si><t>{SecurityElement.Escape(t)}</t></si>")) + "</sst>");
    }

    private static void AppendCell(StringBuilder sheet, string reference, Cell? cell, List<string> strings)
    {
        switch (cell)
        {
            case Text text:
                var index = strings.IndexOf(text.Value);
                if (index < 0)
                {
                    strings.Add(text.Value);
                    index = strings.Count - 1;
                }

                sheet.Append(CultureInfo.InvariantCulture, $"""<c r="{reference}" t="s"><v>{index}</v></c>""");
                break;
            case Number number:
                sheet.Append(CultureInfo.InvariantCulture, $"""<c r="{reference}"><v>{number.Value.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");
                break;
            case Date date:
                var serial = date.Value.DayNumber - new DateOnly(1899, 12, 30).DayNumber;
                sheet.Append(CultureInfo.InvariantCulture, $"""<c r="{reference}" s="1"><v>{serial}</v></c>""");
                break;
        }
    }

    private static string Column(int index) => ((char)('A' + index)).ToString();

    private static void Part(ZipArchive zip, string name, string xml)
    {
        using var stream = zip.CreateEntry(name).Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
    }
}
