using System.Globalization;
using DataTransfer.Contracts.Jobs;
using DataTransfer.Domains.TransferJobs.Features;
using DataTransfer.Resources;
using FluentValidation;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataTransfer.Tests.Features;

/// <summary>Validators over the REAL resx files, in both languages.</summary>
public sealed class ValidatorTests
{
    private static IStringLocalizer<DataTransferResource> Localizer() =>
        new StringLocalizer<DataTransferResource>(new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    private static readonly IOptions<DataTransferOptions> Settings =
        Microsoft.Extensions.Options.Options.Create(new DataTransferOptions { Limits = { MaxFileBytes = 1000 } });

    private static StartImportCommand Import(string key = "catalog.products", string fileName = "a.csv", long size = 10,
        ImportMode mode = ImportMode.DryRun, ImportPolicy policy = ImportPolicy.BestEffort, TransferFormat? format = null) =>
        new(key, fileName, "text/csv", size, Stream.Null, mode, policy, format);

    [Fact]
    public void A_valid_import_passes() =>
        new StartImportCommandValidator(Localizer(), Settings).Validate(Import()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("en", "The import/export definition is required.")]
    [InlineData("ar", "تعريف الاستيراد/التصدير مطلوب.")]
    public void Missing_definition_key_has_a_localized_message(string culture, string expected) =>
        InCulture(culture, () => Errors(new StartImportCommandValidator(Localizer(), Settings), Import(key: "")))
            .Should().Contain(expected);

    [Theory]
    [InlineData("en", "The file is too large.")]
    [InlineData("ar", "حجم الملف كبير جدًا.")]
    public void A_file_over_the_limit_is_refused_before_upload(string culture, string expected) =>
        InCulture(culture, () => Errors(new StartImportCommandValidator(Localizer(), Settings), Import(size: 1001)))
            .Should().Contain(expected);

    [Fact]
    public void Empty_file_unknown_enums_and_long_names_are_refused()
    {
        var validator = new StartImportCommandValidator(Localizer(), Settings);

        Errors(validator, Import(size: 0)).Should().Contain("A non-empty file is required.");
        Errors(validator, Import(mode: (ImportMode)9)).Should().Contain("The import mode is not valid.");
        Errors(validator, Import(policy: (ImportPolicy)9)).Should().Contain("The import policy is not valid.");
        Errors(validator, Import(format: (TransferFormat)9)).Should().Contain("The file format is not valid (use Csv or Xlsx).");
        Errors(validator, Import(fileName: new string('a', 300))).Should().Contain("The file name must not exceed 255 characters.");
    }

    [Fact]
    public void Export_filter_with_too_many_entries_is_refused()
    {
        var filter = Enumerable.Range(0, StartExportCommandValidator.MaxFilterEntries + 1).ToDictionary(i => $"k{i}", i => "v");

        Errors(new StartExportCommandValidator(Localizer()), new StartExportCommand("catalog.products", TransferFormat.Csv, filter))
            .Should().Contain("The export filter has too many entries.");
    }

    [Fact]
    public void Cancel_and_commit_need_an_id()
    {
        Errors(new CancelTransferJobCommandValidator(Localizer()), new CancelTransferJobCommand(Guid.Empty))
            .Should().Contain("Id is required.");
        Errors(new CommitImportCommandValidator(Localizer()), new CommitImportCommand(Guid.Empty))
            .Should().Contain("Id is required.");
    }

    [Theory]
    [InlineData("items.csv", TransferFormat.Csv)]
    [InlineData("ITEMS.CSV", TransferFormat.Csv)]
    [InlineData("items.txt", TransferFormat.Csv)]
    [InlineData("items.xlsx", TransferFormat.Xlsx)]
    public void File_extension_picks_the_format(string fileName, TransferFormat expected) =>
        TransferFormats.FromFileName(fileName).Should().Be(expected);

    [Theory]
    [InlineData("items.xls")]
    [InlineData("items.pdf")]
    [InlineData("items")]
    public void Other_extensions_are_unsupported(string fileName) =>
        ((Action)(() => TransferFormats.FromFileName(fileName))).Should().Throw<InvalidTransferFileException>()
            .Which.ErrorCode.Should().Be("unsupported_format");

    [Theory]
    [InlineData("en", "Required column(s) missing: SKU.")]
    [InlineData("ar", "أعمدة مطلوبة غير موجودة: رمز المنتج.")]
    public void Engine_messages_follow_the_ui_culture(string culture, string expected) =>
        InCulture(culture, () =>
        {
            var act = () => HeaderMap.Build(["name"],
            [
                new TransferColumn("sku", new LocalizedText("SKU", "رمز المنتج"), Required: true),
                new TransferColumn("name", new LocalizedText("Name", "الاسم"))
            ]);
            return act.Should().Throw<InvalidTransferFileException>().Which.Message;
        }).Should().Be(expected);

    [Fact]
    public void Every_english_key_has_an_arabic_translation()
    {
        var english = ReadKeys("DataTransferResource.resx");
        var arabic = ReadKeys("DataTransferResource.ar.resx");

        arabic.Should().BeEquivalentTo(english);
    }

    private static List<string> Errors<T>(IValidator<T> validator, T instance) =>
        validator.Validate(instance).Errors.Select(e => e.ErrorMessage).ToList();

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

    private static HashSet<string> ReadKeys(string file)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Modules", "DataTransfer")))
            dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "Modules", "DataTransfer", "DataTransfer", "Resources", file);
        return System.Xml.Linq.XDocument.Load(path).Root!.Elements("data")
            .Select(e => e.Attribute("name")!.Value).ToHashSet(StringComparer.Ordinal);
    }
}
