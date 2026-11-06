using DataTransfer.Contracts.IntegrationEvents;
using DataTransfer.Domains.TransferJobs.Events;

namespace DataTransfer.Domains.TransferJobs.Models;

/// <summary>
/// One bulk import or export, with its progress. The row is the source of truth for the job: Hangfire only runs it.
/// <list type="bullet">
/// <item><b>Lease.</b> A worker owns a running job through <see cref="LeaseOwner"/> / <see cref="LeaseUntil"/>, claimed
/// and renewed under optimistic concurrency, so two workers (a Hangfire re-fetch of a long job, the stall sweep)
/// never process it at the same time. An expired lease may be taken over.</item>
/// <item><b>Checkpoint.</b> <see cref="LastCommittedRow"/> is the last file row whose batch was saved; a retry
/// resumes after it. Definitions upsert by a natural key, so replaying the last batch is harmless.</item>
/// <item><b>Cancellation.</b> A pending job is cancelled at once; a running one stops between two batches
/// (already committed batches stay committed).</item>
/// </list>
/// Not <c>IAuditable</c>: the job row itself is the record of who imported or exported what, and its progress
/// counters change every batch (pure noise in an audit trail).
/// </summary>
public class TransferJob : Aggregate<Guid>, IMustHaveTenant
{
    public const int DefinitionKeyMaxLength = 100;
    public const int FileNameMaxLength = 255;
    public const int FailureCodeMaxLength = 100;
    public const int FailureMessageMaxLength = 1000;
    public const int FilterMaxLength = 4000;
    public const int LeaseOwnerMaxLength = 200;
    public const int CultureMaxLength = 10;

    public Guid TenantId { get; set; }

    public TransferJobKind Kind { get; private set; }
    public string DefinitionKey { get; private set; } = default!;
    public TransferFormat Format { get; private set; }
    public ImportMode? Mode { get; private set; }
    public ImportPolicy? Policy { get; private set; }
    public TransferJobStatus Status { get; private set; }

    /// <summary>Import: the uploaded file name. Export: the result file name.</summary>
    public string? FileName { get; private set; }

    public Guid? SourceAttachmentId { get; private set; }
    public Guid? ResultAttachmentId { get; private set; }
    public Guid? ErrorReportAttachmentId { get; private set; }

    /// <summary>Commit started from a dry run: the dry-run job (same source file).</summary>
    public Guid? DryRunJobId { get; private set; }

    /// <summary>Export filter as JSON (string key/values).</summary>
    public string? FilterJson { get; private set; }

    /// <summary>UI culture of the requester (row messages, headers, notification).</summary>
    public string Culture { get; private set; } = "en";

    public Guid RequestedBy { get; private set; }

    public long? TotalRows { get; private set; }
    public long ProcessedRows { get; private set; }
    public long CreatedRows { get; private set; }
    public long UpdatedRows { get; private set; }
    public long UnchangedRows { get; private set; }
    public long FailedRows { get; private set; }

    /// <summary>Row errors saved in <c>transfer_job_errors</c> (at most <c>DataTransfer:MaxStoredErrors</c>).</summary>
    public int StoredErrors { get; private set; }

    /// <summary>Checkpoint: the last file row of the last saved batch (0 = nothing processed yet).</summary>
    public long LastCommittedRow { get; private set; }

    /// <summary><see cref="ImportPolicy.ValidateFirst"/>: the whole file passed validation; the commit pass may run.</summary>
    public bool Validated { get; private set; }

    public int Attempts { get; private set; }
    public bool CancelRequested { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseUntil { get; private set; }

    /// <summary>Last time a Hangfire job was queued for this row (creation, retry, stall sweep).</summary>
    public DateTime? LastEnqueuedAt { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    /// <summary>When the job and its files are purged.</summary>
    public DateTime? ExpiresAt { get; private set; }

    public bool IsTerminal => Status is TransferJobStatus.Completed or TransferJobStatus.CompletedWithErrors
        or TransferJobStatus.Failed or TransferJobStatus.Cancelled;

    private TransferJob()
    {
    }

    public static TransferJob CreateImport(Guid id, string definitionKey, TransferFormat format, ImportMode mode,
        ImportPolicy policy, string fileName, Guid sourceAttachmentId, Guid requestedBy, string culture)
    {
        var job = New(id, TransferJobKind.Import, definitionKey, format, requestedBy, culture);
        job.Mode = mode;
        job.Policy = policy;
        job.FileName = TextLimits.Clip(fileName, FileNameMaxLength);
        job.SourceAttachmentId = sourceAttachmentId;
        return job;
    }

    /// <summary>A COMMIT import of the same file as <paramref name="dryRun"/> (which must have completed).</summary>
    public static TransferJob CreateCommitFrom(TransferJob dryRun, ImportPolicy? policy, Guid requestedBy, string culture)
    {
        if (dryRun.Kind != TransferJobKind.Import || dryRun.Mode != ImportMode.DryRun)
            throw new InvalidTransferJobStateException("commit_needs_dry_run");
        if (dryRun.Status is not (TransferJobStatus.Completed or TransferJobStatus.CompletedWithErrors))
            throw new InvalidTransferJobStateException("dry_run_not_completed");
        if (dryRun.SourceAttachmentId is not { } source)
            throw new InvalidTransferJobStateException("dry_run_without_source");

        var job = CreateImport(Guid.CreateVersion7(), dryRun.DefinitionKey, dryRun.Format, ImportMode.Commit,
            policy ?? dryRun.Policy ?? ImportPolicy.BestEffort, dryRun.FileName ?? "import", source, requestedBy, culture);
        job.DryRunJobId = dryRun.Id;
        return job;
    }

    public static TransferJob CreateExport(string definitionKey, TransferFormat format, string? filterJson,
        Guid requestedBy, string culture)
    {
        if (filterJson is { Length: > FilterMaxLength })
            throw new InvalidTransferJobStateException("filter_too_large");

        var job = New(Guid.CreateVersion7(), TransferJobKind.Export, definitionKey, format, requestedBy, culture);
        job.FilterJson = filterJson;
        return job;
    }

    private static TransferJob New(Guid id, TransferJobKind kind, string definitionKey, TransferFormat format,
        Guid requestedBy, string culture)
    {
        if (string.IsNullOrWhiteSpace(definitionKey) || definitionKey.Length > DefinitionKeyMaxLength)
            throw new InvalidTransferJobStateException("definition_required");
        if (requestedBy == Guid.Empty)
            throw new InvalidTransferJobStateException("requester_required");

        return new TransferJob
        {
            Id = id,
            Kind = kind,
            DefinitionKey = definitionKey,
            Format = format,
            Status = TransferJobStatus.Pending,
            RequestedBy = requestedBy,
            Culture = TransferCulture.Normalize(culture)
        };
    }

    // ----- lease ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the job for <paramref name="owner"/>: false when it is finished or another worker holds a live lease.
    /// Saved with the optimistic version, so of two concurrent claims only one commits.
    /// </summary>
    public bool TryClaim(string owner, DateTime now, TimeSpan lease)
    {
        if (IsTerminal)
            return false;
        if (Status == TransferJobStatus.Running && LeaseOwner != owner && LeaseUntil > now)
            return false;

        Status = TransferJobStatus.Running;
        StartedAt ??= now;
        LeaseOwner = TextLimits.Clip(owner, LeaseOwnerMaxLength);
        LeaseUntil = now + lease;
        Attempts++;
        return true;
    }

    public bool HoldsLease(string owner) => Status == TransferJobStatus.Running && LeaseOwner == owner;

    public void RenewLease(DateTime now, TimeSpan lease) => LeaseUntil = now + lease;

    /// <summary>Gives the lease up after a failed attempt, so the Hangfire retry can claim it at once.</summary>
    public void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseUntil = null;
    }

    public void MarkEnqueued(DateTime now) => LastEnqueuedAt = now;

    /// <summary>
    /// Needs to be queued again: not finished, no live lease, and not queued within <paramref name="quietPeriod"/>
    /// (a lost enqueue, a crashed worker, a lease that expired).
    /// </summary>
    public bool IsStalled(DateTime now, TimeSpan quietPeriod) =>
        !IsTerminal &&
        (LeaseUntil is null || LeaseUntil < now) &&
        (LastEnqueuedAt is null || LastEnqueuedAt + quietPeriod < now);

    // ----- progress ---------------------------------------------------------------------------------------------

    public void SetTotalRows(long total) => TotalRows = total;

    /// <summary>
    /// Restarts the progress (a validation pass that is run again from the first row after a failed attempt). The
    /// caller deletes the stored row errors in the same unit of work.
    /// </summary>
    public void ResetProgress()
    {
        ProcessedRows = CreatedRows = UpdatedRows = UnchangedRows = FailedRows = 0;
        StoredErrors = 0;
        LastCommittedRow = 0;
    }

    /// <summary>Adds one batch to the counters and moves the checkpoint to <paramref name="lastRow"/>.</summary>
    public void RecordBatch(BatchCounts counts, long lastRow, int storedErrors)
    {
        ProcessedRows += counts.Total;
        CreatedRows += counts.Created;
        UpdatedRows += counts.Updated;
        UnchangedRows += counts.Unchanged;
        FailedRows += counts.Failed;
        StoredErrors += storedErrors;
        LastCommittedRow = Math.Max(LastCommittedRow, lastRow);
    }

    /// <summary>Export progress (no row outcomes).</summary>
    public void RecordExported(long rows) => ProcessedRows = rows;

    /// <summary>
    /// <see cref="ImportPolicy.ValidateFirst"/>: the validation pass found no invalid row. The counters and checkpoint
    /// restart for the commit pass.
    /// </summary>
    public void MarkValidated()
    {
        Validated = true;
        ResetProgress();
    }

    // ----- cancellation -----------------------------------------------------------------------------------------

    public void RequestCancel(DateTime now, int retentionDays)
    {
        if (IsTerminal)
            throw new InvalidTransferJobStateException("already_finished");

        if (Status == TransferJobStatus.Pending)
        {
            Finish(TransferJobStatus.Cancelled, now, retentionDays);
            return;
        }

        CancelRequested = true;
    }

    /// <summary>The worker saw <see cref="CancelRequested"/> between two batches.</summary>
    public void ConfirmCancelled(DateTime now, int retentionDays) => Finish(TransferJobStatus.Cancelled, now, retentionDays);

    // ----- completion -------------------------------------------------------------------------------------------

    public void AttachResult(Guid attachmentId, string fileName)
    {
        ResultAttachmentId = attachmentId;
        FileName = TextLimits.Clip(fileName, FileNameMaxLength);
    }

    public void AttachErrorReport(Guid attachmentId) => ErrorReportAttachmentId = attachmentId;

    /// <summary>Finished normally: <c>Completed</c>, or <c>CompletedWithErrors</c> when any row failed.</summary>
    public void Complete(DateTime now, int retentionDays)
    {
        var status = FailedRows > 0 ? TransferJobStatus.CompletedWithErrors : TransferJobStatus.Completed;
        Finish(status, now, retentionDays);

        if (Kind == TransferJobKind.Import && Mode == ImportMode.Commit)
        {
            AddIntegrationEvent(new DataImportCompletedIntegrationEvent
            {
                JobId = Id,
                DefinitionKey = DefinitionKey,
                RequestedBy = RequestedBy,
                CreatedRows = CreatedRows,
                UpdatedRows = UpdatedRows,
                FailedRows = FailedRows
            });
        }
    }

    public void Fail(string code, string message, DateTime now, int retentionDays)
    {
        FailureCode = TextLimits.Clip(code, FailureCodeMaxLength);
        FailureMessage = TextLimits.Clip(message, FailureMessageMaxLength);
        Finish(TransferJobStatus.Failed, now, retentionDays);
    }

    private void Finish(TransferJobStatus status, DateTime now, int retentionDays)
    {
        if (IsTerminal)
            throw new InvalidTransferJobStateException("already_finished");

        Status = status;
        FinishedAt = now;
        ExpiresAt = now.AddDays(retentionDays);
        CancelRequested = false;
        ReleaseLease();
        AddDomainEvent(new TransferJobFinishedEvent(Id, Kind, DefinitionKey, status, RequestedBy, Culture));
    }

}

/// <summary>Row outcomes of one batch.</summary>
public readonly record struct BatchCounts(long Created, long Updated, long Unchanged, long Failed)
{
    public long Total => Created + Updated + Unchanged + Failed;

    public static BatchCounts From(IEnumerable<ImportRowResult> results)
    {
        long created = 0, updated = 0, unchanged = 0, failed = 0;
        foreach (var result in results)
        {
            switch (result.Outcome)
            {
                case ImportRowOutcome.Created:
                    created++;
                    break;
                case ImportRowOutcome.Updated:
                    updated++;
                    break;
                case ImportRowOutcome.Unchanged:
                    unchanged++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new BatchCounts(created, updated, unchanged, failed);
    }
}
