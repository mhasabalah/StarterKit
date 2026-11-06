namespace DataTransfer.Contracts.Jobs;

// ----- DTOs -----------------------------------------------------------------------------------------------------

/// <summary>A transfer job as listed. <see cref="Version"/> is the optimistic-concurrency token for commit/cancel.</summary>
public sealed record TransferJobDto(
    Guid Id,
    TransferJobKind Kind,
    string DefinitionKey,
    TransferFormat Format,
    ImportMode? Mode,
    ImportPolicy? Policy,
    TransferJobStatus Status,
    string? FileName,
    long? TotalRows,
    long ProcessedRows,
    long CreatedRows,
    long UpdatedRows,
    long UnchangedRows,
    long FailedRows,
    bool CancelRequested,
    bool HasResult,
    bool HasErrorReport,
    string? FailureCode,
    string? FailureMessage,
    Guid RequestedBy,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    DateTime? ExpiresAt,
    long Version);

/// <summary>One stored row error (the first ones of a job; the full list is the error report file).</summary>
public sealed record TransferJobErrorDto(long RowNumber, string? Column, string Code, string Message);

public sealed record TransferJobDetailDto(TransferJobDto Job, IReadOnlyList<TransferJobErrorDto> FirstErrors);

/// <summary>An available definition (only those the caller may use are listed).</summary>
public sealed record TransferDefinitionDto(
    string Key,
    LocalizedText DisplayName,
    bool CanImport,
    bool CanExport,
    IReadOnlyList<TransferColumnDto> Columns);

public sealed record TransferColumnDto(
    string Name,
    LocalizedText Header,
    bool Required,
    string? Sample,
    LocalizedText? Description,
    TransferColumnKind Kind);

// ----- commands -------------------------------------------------------------------------------------------------

/// <summary>
/// Stores the uploaded file (through Attachment: content validation + malware scan) and queues an import job.
/// <see cref="Format"/> defaults to the file extension.
/// </summary>
public sealed record StartImportCommand(
    string DefinitionKey,
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileStream,
    ImportMode Mode = ImportMode.DryRun,
    ImportPolicy Policy = ImportPolicy.BestEffort,
    TransferFormat? Format = null) : ICommand<TransferJobDto>;

/// <summary>
/// Starts a COMMIT import of the same source file as a finished dry run (no second upload). The dry run must have
/// completed. A second commit while the first is still pending or running is refused (409 <c>import_already_running</c>).
/// </summary>
public sealed record CommitImportCommand(Guid DryRunJobId, ImportPolicy? Policy = null) : ICommand<TransferJobDto>;

/// <param name="Filter">Definition-specific filter (string key/values).</param>
public sealed record StartExportCommand(
    string DefinitionKey,
    TransferFormat Format = TransferFormat.Xlsx,
    IReadOnlyDictionary<string, string>? Filter = null) : ICommand<TransferJobDto>;

/// <summary>
/// Requests cancellation (idempotent, no version: a running job saves its row every batch, and a stop request cannot
/// lose anyone's edit). A pending job is cancelled at once, a running one between two batches; a finished job is 400.
/// </summary>
public sealed record CancelTransferJobCommand(Guid JobId) : ICommand<TransferJobDto>;

// ----- queries --------------------------------------------------------------------------------------------------

/// <summary>Jobs of the current tenant, newest first. Without the manage permission, only the caller's own jobs.</summary>
public sealed record GetTransferJobsQuery : BaseQuery, IQuery<PaginatedResult<TransferJobDto>>
{
    public TransferJobKind? Kind { get; init; }
    public TransferJobStatus? Status { get; init; }
    public string? DefinitionKey { get; init; }
}

public sealed record GetTransferJobByIdQuery(Guid JobId) : IQuery<TransferJobDetailDto>;

/// <summary>A short-lived signed URL of one of the job's files.</summary>
public sealed record GetTransferJobFileQuery(Guid JobId, TransferJobFileKind Kind) : IQuery<TransferJobFileResult>;

public sealed record TransferJobFileResult(string Url, DateTime ExpiresAt, string FileName, string ContentType);

public sealed record GetTransferDefinitionsQuery : IQuery<IReadOnlyList<TransferDefinitionDto>>;

/// <summary>An empty file with the definition's localized headers and one sample row.</summary>
public sealed record GetTransferTemplateQuery(string DefinitionKey, TransferFormat Format = TransferFormat.Xlsx)
    : IQuery<TransferTemplateResult>;

public sealed record TransferTemplateResult(byte[] Content, string FileName, string ContentType);
