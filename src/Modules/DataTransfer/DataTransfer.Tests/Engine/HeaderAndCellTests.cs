namespace DataTransfer.Tests.Engine;

public sealed class HeaderMapTests
{
    private static readonly IReadOnlyList<TransferColumn> Columns =
    [
        new("sku", new LocalizedText("SKU", "رمز المنتج"), Required: true),
        new("name_en", new LocalizedText("Name (English)", "الاسم (إنجليزي)"), Required: true),
        new("price", new LocalizedText("Price", "السعر"))
    ];

    [Fact]
    public void Headers_match_by_name_english_or_arabic_ignoring_case_spaces_bom_and_required_marker()
    {
        var map = HeaderMap.Build(["﻿  sku ", "الاسم   (إنجليزي) *", "PRICE", "Notes"], Columns);

        map.Columns.Should().Equal((0, "sku"), (1, "name_en"), (2, "price"));
        map.IgnoredHeaders.Should().Equal("Notes");
    }

    [Fact]
    public void Missing_required_column_fails_the_file_with_missing_columns()
    {
        var act = () => HeaderMap.Build(["sku", "price"], Columns);

        act.Should().Throw<InvalidTransferFileException>()
            .Where(e => e.ErrorCode == "missing_columns" && e.Message.Contains("Name (English)"));
    }

    [Fact]
    public void Same_column_twice_fails_with_duplicate_column()
    {
        var act = () => HeaderMap.Build(["sku", "SKU", "name_en"], Columns);

        act.Should().Throw<InvalidTransferFileException>().Where(e => e.ErrorCode == "duplicate_column");
    }

    [Fact]
    public void Header_without_any_known_column_fails_with_no_known_columns()
    {
        var optional = new[] { new TransferColumn("note", new LocalizedText("Note", "ملاحظة")) };

        var act = () => HeaderMap.Build(["foo", "bar"], optional);

        act.Should().Throw<InvalidTransferFileException>().Where(e => e.ErrorCode == "no_known_columns");
    }
}

public sealed class CellTextTests
{
    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("@x", "'@x")]
    [InlineData("-x", "'-x")]
    [InlineData("-5", "-5")]
    [InlineData("+3.25", "+3.25")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void EscapeFormula_guards_formulas_but_keeps_numbers(string input, string expected) =>
        CellText.EscapeFormula(input).Should().Be(expected);

    [Theory]
    [InlineData("  value  ", "value")]
    [InlineData("   ", null)]
    [InlineData("'=SUM(A1)", "=SUM(A1)")]
    [InlineData("'quoted", "'quoted")]
    public void Clean_trims_blanks_to_null_and_removes_the_guard_apostrophe(string input, string? expected) =>
        CellText.Clean(input).Should().Be(expected);

    [Fact]
    public void Exported_formula_text_reimports_unchanged() =>
        CellText.Clean(CellText.EscapeFormula("=HYPERLINK(\"x\")")).Should().Be("=HYPERLINK(\"x\")");

    [Fact]
    public void ToXmlSafe_replaces_control_characters_and_keeps_surrogate_pairs() =>
        CellText.ToXmlSafe("a\u0001b😀").Should().Be("a�b😀");
}

public sealed class ImportRecordsTests
{
    private static readonly HeaderMap Map = HeaderMap.Build(["sku", "name"],
        [new TransferColumn("sku", new LocalizedText("SKU", "ر")), new TransferColumn("name", new LocalizedText("Name", "اسم"))]);

    [Fact]
    public void Build_keys_values_by_column_and_turns_blank_or_missing_cells_into_null()
    {
        var (record, error) = ImportRecords.Build(Map, new TabularRow(7, [" A-1 ", "  "]), 100);

        error.Should().BeNull();
        record.RowNumber.Should().Be(7);
        record.Get("sku").Should().Be("A-1");
        record.Get("name").Should().BeNull();
    }

    [Fact]
    public void A_cell_over_the_length_limit_fails_the_row_with_cell_too_long()
    {
        var (_, error) = ImportRecords.Build(Map, new TabularRow(2, ["A", new string('x', 11)]), 10);

        error.Should().NotBeNull();
        error!.Code.Should().Be("cell_too_long");
        error.Column.Should().Be("name");
    }

    [Fact]
    public void Complete_gives_rows_skipped_by_a_definition_a_no_result_failure()
    {
        var records = new[] { Record(2), Record(3) };
        var applied = new[] { ImportRowResult.Created(2) };

        var results = DataTransfer.Domains.TransferJobs.Jobs.ProcessImportJob.Complete(records, applied).ToList();

        results.Should().HaveCount(2);
        results[1].Outcome.Should().Be(ImportRowOutcome.Failed);
        results[1].Errors.Single().Code.Should().Be("no_result");
    }

    [Fact]
    public void BatchCounts_counts_every_outcome()
    {
        var counts = BatchCounts.From([
            ImportRowResult.Created(2), ImportRowResult.Updated(3), ImportRowResult.Unchanged(4),
            ImportRowResult.Failed(5, null, "x", "y"), ImportRowResult.Created(6)
        ]);

        counts.Should().Be(new BatchCounts(2, 1, 1, 1));
        counts.Total.Should().Be(5);
    }

    private static ImportRecord Record(long row) => new(row, new Dictionary<string, string?>());
}
