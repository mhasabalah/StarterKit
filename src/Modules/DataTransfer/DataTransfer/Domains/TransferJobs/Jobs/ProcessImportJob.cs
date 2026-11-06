using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataTransfer.Domains.TransferJobs.Jobs;

/// <summary>
/// Runs one import job on the <c>data-transfer</c> queue.
/// <list type="number">
/// <item>Copy the source attachment to a scratch file; count the rows (first run): header mapping and limits fail the
/// file before anything is written.</item>
/// <item><see cref="ImportPolicy.ValidateFirst"/> commit: a dry-run pass over the whole file; any invalid row fails the
/// job (<c>validation_failed</c>) with nothing written.</item>
/// <item>The main pass, in batches of <see cref="DataTransferOptions.BatchSize"/>: each batch is ONE unit of work of the
/// owning module's definition (its events and outbox are dispatched on commit), then the checkpoint, counters and row
/// errors are saved under the lease, and a cancel request is honoured.</item>
/// <item>The error report (CSV) is built from the stored errors; the job completes.</item>
/// </list>
/// A retry resumes after <see cref="TransferJob.LastCommittedRow"/>.
/// </summary>
[Queue(DataTransferOptions.Queue)]
[AutomaticRetry(Attempts = 0)]
public sealed class ProcessImportJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantContext tenantContext,
    IBackgroundJobClient jobs,
    TransferFileStore files,
    TimeProvider timeProvider,
    IOptions<DataTransferOptions> options,
    DataTransferMetrics metrics,
    TransferProgressPublisher progress,
    ILogger<ProcessImportJob> logger)
    : TransferJobRunner(unitOfWork, tenantContext, jobs, timeProvider, options, metrics, progress, logger)
{
    private sealed record Pass(TransferJob Job, string Owner, TempFile File, IReadOnlyList<TransferColumn> Columns, bool DryRun);

    public Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken) => ExecuteCoreAsync(jobId, cancellationToken);

    protected override void ScheduleRetry(Guid jobId, TimeSpan delay) =>
        Jobs.Schedule<ProcessImportJob>(job => job.ExecuteAsync(jobId, CancellationToken.None), delay);

    protected override async Task RunAsync(TransferJob claimed, string owner, CancellationToken cancellationToken)
    {
        if (claimed.SourceAttachmentId is not { } sourceId)
            throw new InvalidTransferFileException("missing_source");

        var columns = await UnitOfWork.RunAsync((services, _) => Task.FromResult(
            services.GetRequiredService<TransferDefinitionRegistry>().GetImport(claimed.DefinitionKey).Columns), cancellationToken);

        await using var file = await files.DownloadAsync(sourceId, TabularWriter.Extension(claimed.Format),
            Settings.Limits.MaxFileBytes, cancellationToken);

        await CheckHeaderAsync(claimed, owner, file, columns, cancellationToken);

        var job = claimed;
        if (job is { Mode: ImportMode.Commit, Policy: ImportPolicy.ValidateFirst, Validated: false })
        {
            job = await ValidateAllAsync(new Pass(job, owner, file, columns, DryRun: true), cancellationToken);
            if (job.IsTerminal)
                return;
        }

        var completed = await RunPassAsync(new Pass(job, owner, file, columns, job.Mode == ImportMode.DryRun),
            job.LastCommittedRow, cancellationToken);
        if (completed)
            await FinishAsync(job, owner, validationFailed: false, cancellationToken);
    }

    /// <summary>
    /// Header check before any write (bad columns fail the file at once) WITHOUT reading the rows: the total is the
    /// sheet dimension when the file announces one (XLSX), and the exact count at the end. Limits are enforced while
    /// the rows stream.
    /// </summary>
    private async Task CheckHeaderAsync(TransferJob job, string owner, TempFile file, IReadOnlyList<TransferColumn> columns,
        CancellationToken cancellationToken)
    {
        await using var reader = await TabularReader.OpenAsync(file.Path, job.Format, Settings.Limits, cancellationToken);
        HeaderMap.Build(reader.Header, columns);

        if (job.TotalRows is null && reader.EstimatedRows is { } estimate)
            await UpdateUnderLeaseAsync(job.Id, owner, j => j.SetTotalRows(estimate));
    }

    /// <summary>ValidateFirst: dry-run pass from row 1. Returns the job, terminal when validation failed or it was cancelled.</summary>
    private async Task<TransferJob> ValidateAllAsync(Pass pass, CancellationToken cancellationToken)
    {
        await UpdateUnderLeaseAsync(pass.Job.Id, pass.Owner, (job, _) =>
        {
            job.ResetProgress();
            return true;
        });
        await UnitOfWork.RunAsync((services, ct) => services.GetRequiredService<DataTransferDbContext>().Errors
            .Where(e => e.JobId == pass.Job.Id).ExecuteDeleteAsync(ct), cancellationToken);

        if (!await RunPassAsync(pass, afterRow: 0, cancellationToken))
            return await ReloadAsync(pass.Job.Id);

        var failed = await UpdateUnderLeaseAsync(pass.Job.Id, pass.Owner, (job, _) =>
        {
            if (job.FailedRows > 0)
                return job.FailedRows;
            job.MarkValidated();
            return 0;
        });

        if (failed > 0)
            await FinishAsync(pass.Job, pass.Owner, validationFailed: true, cancellationToken);
        return await ReloadAsync(pass.Job.Id);
    }

    /// <summary>Reads the file after <paramref name="afterRow"/> in batches. False when the job was cancelled.</summary>
    private async Task<bool> RunPassAsync(Pass pass, long afterRow, CancellationToken cancellationToken)
    {
        await using var reader = await TabularReader.OpenAsync(pass.File.Path, pass.Job.Format, Settings.Limits,
            cancellationToken);
        var map = HeaderMap.Build(reader.Header, pass.Columns);

        var records = new List<ImportRecord>(Settings.BatchSize);
        var rejected = new List<ImportRowResult>();
        var lastRow = afterRow;

        await foreach (var row in reader.ReadRowsAsync(afterRow, cancellationToken))
        {
            var (record, error) = ImportRecords.Build(map, row, Settings.Limits.MaxCellLength);
            if (error is null)
                records.Add(record);
            else
                rejected.Add(ImportRowResult.Failed(row.RowNumber, [error]));
            lastRow = row.RowNumber;

            if (records.Count + rejected.Count < Settings.BatchSize)
                continue;
            if (!await ProcessBatchAsync(pass, records, rejected, lastRow, cancellationToken))
                return false;
            records.Clear();
            rejected.Clear();
        }

        return records.Count + rejected.Count == 0 ||
               await ProcessBatchAsync(pass, records, rejected, lastRow, cancellationToken);
    }

    private async Task<bool> ProcessBatchAsync(Pass pass, List<ImportRecord> records, List<ImportRowResult> rejected,
        long lastRow, CancellationToken cancellationToken)
    {
        var applied = records.Count == 0 ? [] : await ApplyAsync(pass, records, cancellationToken);
        var results = Complete(records, applied).Concat(rejected).ToList();
        var counts = BatchCounts.From(results);
        Metrics.RecordRows(pass.Job.DefinitionKey, counts);

        var errors = results
            .Where(r => r.Outcome == ImportRowOutcome.Failed)
            .SelectMany(r => r.Errors.Count == 0
                ? [TransferJobError.Create(pass.Job.Id, r.RowNumber, new ImportRowError(null, "invalid", TransferText.Get("Transfer.Row.invalid")))]
                : r.Errors.Select(e => TransferJobError.Create(pass.Job.Id, r.RowNumber, e)))
            .ToList();

        return await UpdateUnderLeaseAsync(pass.Job.Id, pass.Owner, (job, repository) =>
        {
            var room = Math.Max(0, Settings.MaxStoredErrors - job.StoredErrors);
            var stored = errors.Take(room).ToList();
            repository.AddErrors(stored);
            job.RecordBatch(counts, lastRow, stored.Count);
            return ContinueOrCancel(job);
        });
    }

    /// <summary>One unit of work of the owning module: its saves, domain events and outbox rows commit together.</summary>
    private Task<IReadOnlyList<ImportRowResult>> ApplyAsync(Pass pass, List<ImportRecord> records,
        CancellationToken cancellationToken)
    {
        var context = new ImportContext(pass.Job.Id, pass.Job.RequestedBy, pass.DryRun, pass.Job.Culture);
        var batch = records.ToList();
        return UnitOfWork.RunAsync(async (services, ct) =>
        {
            // Plan quota: only rows that may be written count (a dry run is free). Checked BEFORE the batch is applied.
            if (!pass.DryRun)
                await ConsumeRowsAsync(services, batch.Count, ct);
            return await services.GetRequiredService<TransferDefinitionRegistry>()
                .GetImport(pass.Job.DefinitionKey)
                .ApplyBatchAsync(batch, context, ct);
        }, cancellationToken);
    }

    /// <summary>Guards against a definition that skipped rows: every record gets exactly one result.</summary>
    internal static IEnumerable<ImportRowResult> Complete(IReadOnlyList<ImportRecord> records,
        IReadOnlyList<ImportRowResult> applied)
    {
        var byRow = new Dictionary<long, ImportRowResult>();
        foreach (var result in applied)
            byRow.TryAdd(result.RowNumber, result);

        return records.Select(r => byRow.TryGetValue(r.RowNumber, out var result)
            ? result
            : ImportRowResult.Failed(r.RowNumber, null, "no_result", TransferText.Get("Transfer.Row.no_result")));
    }

    private async Task FinishAsync(TransferJob job, string owner, bool validationFailed, CancellationToken cancellationToken)
    {
        var stored = await UnitOfWork.RunAsync((services, ct) => services.GetRequiredService<DataTransferDbContext>().Errors
            .CountAsync(e => e.JobId == job.Id, ct), cancellationToken);
        Guid? reportId = stored > 0 ? await BuildErrorReportAsync(job, cancellationToken) : null;

        await UpdateUnderLeaseAsync(job.Id, owner, j =>
        {
            j.SetTotalRows(j.ProcessedRows); // exact, now that every row was read
            if (reportId is { } id)
                j.AttachErrorReport(id);
            if (validationFailed)
            {
                j.Fail("validation_failed", TransferText.Get("Transfer.Job.validation_failed", j.FailedRows), Now,
                    Settings.RetentionDays);
            }
            else
            {
                j.Complete(Now, Settings.RetentionDays);
            }
        });

        Logger.LogInformation("Import {JobId} ({Definition}) finished: validation failed {ValidationFailed}",
            job.Id, job.DefinitionKey, validationFailed);
    }

    /// <summary>Streams the stored errors (row order) into a CSV attachment of the job.</summary>
    private async Task<Guid> BuildErrorReportAsync(TransferJob job, CancellationToken cancellationToken)
    {
        await using var report = TempFile.Create(".csv");
        await using (var stream = report.OpenWrite())
        {
            await using var writer = TabularWriter.Create(stream, TransferFormat.Csv);
            await ErrorReport.WriteHeaderAsync(writer, job.Culture, cancellationToken);
            await UnitOfWork.RunAsync(async (services, ct) =>
            {
                var errors = services.GetRequiredService<DataTransferDbContext>().Errors.AsNoTracking()
                    .Where(e => e.JobId == job.Id)
                    .OrderBy(e => e.RowNumber).ThenBy(e => e.Id)
                    .AsAsyncEnumerable();
                await foreach (var error in errors.WithCancellation(ct))
                    await ErrorReport.WriteAsync(writer, error, ct);
            }, cancellationToken);
            await writer.CompleteAsync(cancellationToken);
        }

        var name = $"{job.DefinitionKey}-errors-{Now:yyyyMMdd-HHmmss}.csv";
        return await files.UploadAsync(job.Id, report, name, "text/csv", job.RequestedBy, cancellationToken);
    }

    private Task<TransferJob> ReloadAsync(Guid jobId) =>
        UnitOfWork.RunAsync(async (services, ct) =>
            await services.GetRequiredService<DataTransferDbContext>().Jobs.AsNoTracking().FirstAsync(j => j.Id == jobId, ct),
            CancellationToken.None);
}
