using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DataTransfer.Tests.Features;

/// <summary>Arabic and English everywhere a user reads DataTransfer text: API errors, job states, files.</summary>
public sealed partial class BilingualTests
{
    private static T InCulture<T>(string culture, Func<T> work)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return work();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("en", "An import of 'catalog.products' is already pending or running.")]
    [InlineData("ar", "يوجد استيراد لـ 'catalog.products' قيد الانتظار أو التنفيذ بالفعل.")]
    public void Api_errors_are_localized(string culture, string expected) =>
        InCulture(culture, () => new ImportAlreadyRunningException("catalog.products").Message).Should().Be(expected);

    [Theory]
    [InlineData("en", "The job has already finished.")]
    [InlineData("ar", "انتهت المهمة بالفعل.")]
    public void Job_state_errors_from_the_aggregate_are_localized(string culture, string expected)
    {
        var job = TransferJob.CreateExport("x.y", TransferFormat.Csv, null, Guid.NewGuid(), culture);
        job.RequestCancel(DateTime.UtcNow, 7);

        InCulture(culture, () => ((Action)(() => job.RequestCancel(DateTime.UtcNow, 7)))
                .Should().Throw<InvalidTransferJobStateException>().Which.Message)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("en", "The job has no error report.")]
    [InlineData("ar", "لا يوجد تقرير أخطاء لهذه المهمة.")]
    public void Missing_file_errors_name_the_file_in_the_users_language(string culture, string expected) =>
        InCulture(culture, () => new TransferJobFileNotFoundException(TransferJobFileKind.ErrorReport).Message)
            .Should().Be(expected);

    [Fact]
    public void Every_resource_key_the_code_uses_exists()
    {
        var keys = ReadKeys("DataTransferResource.resx");
        var used = UsedKeys().ToList();

        used.Should().NotBeEmpty();
        used.Where(k => !keys.Contains(k)).Should().BeEmpty("a missing key would show its raw name to the user");
    }

    [Fact]
    public async Task A_broken_quote_deep_in_a_csv_fails_the_file_with_its_row_and_no_parser_text()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dt-bad-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "sku,name\nA,ok\nB,ok\nC,\"never closed\nD,x\n");
        try
        {
            await using var reader = await TabularReader.OpenAsync(path, TransferFormat.Csv, new DataTransferLimits(),
                CancellationToken.None);
            var act = async () =>
            {
                await foreach (var _ in reader.ReadRowsAsync(0, CancellationToken.None))
                {
                }
            };

            var error = (await act.Should().ThrowAsync<InvalidTransferFileException>()).Which;
            error.ErrorCode.Should().Be("invalid_csv");
            error.Message.Should().StartWith("The CSV file cannot be read near row");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("ar", "1", "البيانات")]
    [InlineData("en", null, "Data")]
    public async Task Arabic_workbooks_are_right_to_left_with_an_arabic_sheet_name(string culture, string? rtl, string sheet)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dt-rtl-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using (var stream = File.Create(path))
            await using (var writer = TabularWriter.Create(stream, TransferFormat.Xlsx, [], culture))
            {
                await writer.WriteRowAsync(["رمز المنتج"], CancellationToken.None);
                await writer.CompleteAsync(CancellationToken.None);
            }

            using var zip = ZipFile.OpenRead(path);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using (var sheetXml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open())
                XDocument.Load(sheetXml).Descendants(ns + "sheetView").Single().Attribute("rightToLeft")?.Value.Should().Be(rtl);
            using (var workbookXml = zip.GetEntry("xl/workbook.xml")!.Open())
                XDocument.Load(workbookXml).Descendants(ns + "sheet").Single().Attribute("name")!.Value.Should().Be(sheet);

            await using var reader = await TabularReader.OpenAsync(path, TransferFormat.Xlsx, new DataTransferLimits(),
                CancellationToken.None);
            reader.Header.Should().Equal("رمز المنتج");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Resource keys referenced from the DataTransfer sources: literal keys, job-state reasons, file codes.</summary>
    private static IEnumerable<string> UsedKeys()
    {
        var sources = Directory.EnumerateFiles(ProjectDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        foreach (var source in sources)
        {
            foreach (Match m in LiteralKey().Matches(source))
                yield return m.Groups[1].Value;
            foreach (Match m in StateReason().Matches(source))
                yield return "Transfer.State." + m.Groups[1].Value;
            foreach (Match m in FileCode().Matches(source))
                yield return "Transfer.File." + m.Groups[1].Value;
        }

        foreach (var kind in Enum.GetNames<TransferJobFileKind>())
            yield return "Transfer.FileKind." + kind;
    }

    [GeneratedRegex("\"(Transfer\\.[A-Za-z]+\\.[A-Za-z_]+)\"")]
    private static partial Regex LiteralKey();

    [GeneratedRegex("InvalidTransferJobStateException\\(\"([a-z_]+)\"\\)")]
    private static partial Regex StateReason();

    // Single-code form only: the two-argument form passes its own literal key, covered by LiteralKey.
    [GeneratedRegex("InvalidTransferFileException\\(\"([a-z_]+)\"(?:\\)|, (?!\"Transfer))")]
    private static partial Regex FileCode();

    private static string ProjectDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Modules", "DataTransfer")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src", "Modules", "DataTransfer", "DataTransfer");
    }

    private static HashSet<string> ReadKeys(string file) =>
        XDocument.Load(Path.Combine(ProjectDir(), "Resources", file)).Root!.Elements("data")
            .Select(e => e.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
}
