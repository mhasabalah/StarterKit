namespace DataTransfer.Contracts.Enums;

/// <summary>Direction of a transfer job.</summary>
public enum TransferJobKind
{
    [LocalizedDescription("Import", "استيراد")]
    Import = 1,

    [LocalizedDescription("Export", "تصدير")]
    Export = 2
}

/// <summary>File format of a source (import) or result (export).</summary>
public enum TransferFormat
{
    /// <summary>UTF-8 CSV (RFC 4180). Exports are written with a BOM so Excel shows Arabic correctly.</summary>
    [LocalizedDescription("CSV", "CSV")]
    Csv = 1,

    /// <summary>Excel workbook (.xlsx, first worksheet).</summary>
    [LocalizedDescription("Excel", "إكسل")]
    Xlsx = 2
}

/// <summary>What an import job does with the rows.</summary>
public enum ImportMode
{
    /// <summary>Parse and validate every row (including database checks such as uniqueness); write nothing.</summary>
    [LocalizedDescription("Dry run", "تجربة دون حفظ")]
    DryRun = 1,

    /// <summary>Write the valid rows.</summary>
    [LocalizedDescription("Commit", "تنفيذ")]
    Commit = 2
}

/// <summary>How an import treats invalid rows.</summary>
public enum ImportPolicy
{
    /// <summary>Write the valid rows, skip and report the invalid ones.</summary>
    [LocalizedDescription("Skip invalid rows", "تخطي الصفوف غير الصالحة")]
    BestEffort = 1,

    /// <summary>Validate the whole file first; write nothing when any row is invalid.</summary>
    [LocalizedDescription("All rows must be valid", "يجب أن تكون كل الصفوف صالحة")]
    ValidateFirst = 2
}

/// <summary>Lifecycle of a transfer job.</summary>
public enum TransferJobStatus
{
    /// <summary>Queued, not started yet.</summary>
    [LocalizedDescription("Pending", "قيد الانتظار")]
    Pending = 1,

    [LocalizedDescription("Running", "قيد التنفيذ")]
    Running = 2,

    /// <summary>Every row succeeded.</summary>
    [LocalizedDescription("Completed", "مكتمل")]
    Completed = 3,

    /// <summary>Finished, but some rows failed (see the error report).</summary>
    [LocalizedDescription("Completed with errors", "مكتمل مع أخطاء")]
    CompletedWithErrors = 4,

    /// <summary>The job itself failed (unreadable file, wrong columns, limits, infrastructure).</summary>
    [LocalizedDescription("Failed", "فشل")]
    Failed = 5,

    /// <summary>Cancelled by the user. Batches committed before the cancellation stay committed.</summary>
    [LocalizedDescription("Cancelled", "ملغى")]
    Cancelled = 6
}

/// <summary>Result of one imported row.</summary>
public enum ImportRowOutcome
{
    [LocalizedDescription("Created", "تم الإنشاء")]
    Created = 1,

    [LocalizedDescription("Updated", "تم التحديث")]
    Updated = 2,

    /// <summary>Valid, nothing to change (or a dry run).</summary>
    [LocalizedDescription("Unchanged", "بدون تغيير")]
    Unchanged = 3,

    [LocalizedDescription("Failed", "فشل")]
    Failed = 4
}

/// <summary>
/// What a column holds, for writing XLSX cells. Imports always get text (numbers and dates arrive as invariant strings,
/// e.g. <c>12.5</c>, <c>2026-11-06</c>); exports and templates write real Excel numbers and dates for these kinds.
/// </summary>
public enum TransferColumnKind
{
    /// <summary>Kept as text in Excel (codes, names, anything with leading zeros).</summary>
    [LocalizedDescription("Text", "نص")]
    Text = 1,

    /// <summary>A number (invariant: dot decimal separator). Written as a numeric cell.</summary>
    [LocalizedDescription("Number", "رقم")]
    Number = 2,

    /// <summary>A date (<c>yyyy-MM-dd</c>) or date-time (ISO 8601). Written as an Excel date.</summary>
    [LocalizedDescription("Date", "تاريخ")]
    Date = 3
}

/// <summary>Which file of a job to download.</summary>
public enum TransferJobFileKind
{
    /// <summary>The uploaded import file.</summary>
    [LocalizedDescription("Source file", "الملف المصدر")]
    Source = 1,

    /// <summary>The export result.</summary>
    [LocalizedDescription("Result file", "ملف النتيجة")]
    Result = 2,

    /// <summary>CSV of every failed row (row number, column, code, message).</summary>
    [LocalizedDescription("Error report", "تقرير الأخطاء")]
    ErrorReport = 3
}
