namespace DataTransfer.Options;

/// <summary>Bound from the <c>"DataTransfer"</c> appsettings section. See wiki/Modules/DataTransfer.md.</summary>
public sealed class DataTransferOptions
{
    public const string SectionName = "DataTransfer";

    /// <summary>Hangfire queue of the import/export jobs (served by the <c>Hangfire:Servers:DataTransfer</c> server).</summary>
    public const string Queue = "data-transfer";

    /// <summary>Rows handed to a definition per unit of work (one transaction, one checkpoint).</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>A running job renews its lease every batch; another worker may take over once it expires.</summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>Attempts (first run included) before a job that keeps failing is marked <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Row errors stored per job (the error report contains these; further errors are only counted).</summary>
    public int MaxStoredErrors { get; set; } = 10_000;

    /// <summary>Errors returned with the job detail.</summary>
    public int ErrorPreviewCount { get; set; } = 50;

    /// <summary>Jobs and their files are deleted this many days after they finished.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>A pending job not started after this many minutes (lost enqueue) is queued again by the sweep.</summary>
    public int StalledAfterMinutes { get; set; } = 10;

    public string SweepCron { get; set; } = "*/5 * * * *";

    public string PurgeCron { get; set; } = "30 3 * * *";

    /// <summary>Export: progress is saved every this many rows (and cancellation checked).</summary>
    public int ExportProgressEvery { get; set; } = 5_000;

    /// <summary>Live progress (hub event <c>datatransfer.job-progress</c>): at most one per job per this interval.</summary>
    public int ProgressIntervalMilliseconds { get; set; } = 1_000;

    public DataTransferLimits Limits { get; set; } = new();
}

/// <summary>Input limits, checked before and while parsing (abuse and zip-bomb guard).</summary>
public sealed class DataTransferLimits
{
    /// <summary>Largest accepted upload (the Attachment module's own size limit applies too).</summary>
    public long MaxFileBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Import: data rows per file.</summary>
    public long MaxRows { get; set; } = 200_000;

    /// <summary>Export: rows per file (an .xlsx sheet holds at most 1,048,575 anyway).</summary>
    public long MaxExportRows { get; set; } = 1_000_000;

    public int MaxColumns { get; set; } = 100;

    public int MaxCellLength { get; set; } = 10_000;

    /// <summary>XLSX: total uncompressed size of the zip entries.</summary>
    public long MaxUncompressedBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>XLSX: number of zip entries.</summary>
    public int MaxZipEntries { get; set; } = 1_000;
}
