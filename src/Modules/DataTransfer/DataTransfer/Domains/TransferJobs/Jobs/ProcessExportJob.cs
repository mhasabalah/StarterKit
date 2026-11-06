using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DataTransfer.Domains.TransferJobs.Jobs;

/// <summary>
/// Runs one export job on the <c>data-transfer</c> queue: the owning module's definition streams its rows (one read
/// unit of work), they are written straight into a scratch CSV/XLSX file, progress is saved every
/// <see cref="DataTransferOptions.ExportProgressEvery"/> rows (a cancel request stops it there), and the file becomes a
/// private attachment of the job. A retry starts over (exports write nothing but their own file).
/// </summary>
[Queue(DataTransferOptions.Queue)]
[AutomaticRetry(Attempts = 0)]
public sealed class ProcessExportJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantContext tenantContext,
    IBackgroundJobClient jobs,
    TransferFileStore files,
    TimeProvider timeProvider,
    IOptions<DataTransferOptions> options,
    DataTransferMetrics metrics,
    TransferProgressPublisher progress,
    ILogger<ProcessExportJob> logger)
    : TransferJobRunner(unitOfWork, tenantContext, jobs, timeProvider, options, metrics, progress, logger)
{
    public Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken) => ExecuteCoreAsync(jobId, cancellationToken);

    protected override void ScheduleRetry(Guid jobId, TimeSpan delay) =>
        Jobs.Schedule<ProcessExportJob>(job => job.ExecuteAsync(jobId, CancellationToken.None), delay);

    protected override async Task RunAsync(TransferJob claimed, string owner, CancellationToken cancellationToken)
    {
        await using var file = TempFile.Create(TabularWriter.Extension(claimed.Format));
        var kinds = await KindsOfAsync(claimed);

        long rows;
        await using (var stream = file.OpenWrite())
        {
            await using var writer = TabularWriter.Create(stream, claimed.Format, kinds, claimed.Culture);
            rows = await WriteRowsAsync(claimed, owner, writer, cancellationToken);
            if (rows < 0)
                return; // cancelled (already saved as Cancelled)
            await writer.CompleteAsync(cancellationToken);
        }

        // Attachment refuses documents over its limit (Attachment:Validation:MaxDocumentSizeBytes, matched by
        // DataTransfer:Limits:MaxFileBytes): fail now with a clear code instead of three full exports.
        var size = new FileInfo(file.Path).Length;
        if (size > Settings.Limits.MaxFileBytes)
            throw new InvalidTransferFileException("file_too_large", "Transfer.Export.too_large", [Settings.Limits.MaxFileBytes]);

        var fileName = $"{claimed.DefinitionKey}-{Now:yyyyMMdd-HHmmss}{TabularWriter.Extension(claimed.Format)}";
        var attachmentId = await files.UploadAsync(claimed.Id, file, fileName, TabularWriter.ContentType(claimed.Format),
            claimed.RequestedBy, cancellationToken);

        await UpdateUnderLeaseAsync(claimed.Id, owner, job =>
        {
            job.AttachResult(attachmentId, fileName);
            job.RecordExported(rows);
            job.SetTotalRows(rows);
            job.Complete(Now, Settings.RetentionDays);
        });

        Logger.LogInformation("Export {JobId} ({Definition}) finished: {Rows} rows", claimed.Id, claimed.DefinitionKey, rows);
    }

    /// <summary>Header + every row; the row count, or -1 when the job was cancelled.</summary>
    private Task<long> WriteRowsAsync(TransferJob job, string owner, TabularWriter writer, CancellationToken cancellationToken)
    {
        var context = new ExportContext(job.Id, job.RequestedBy, ParseFilter(job.FilterJson), job.Culture);
        var maxRows = job.Format == TransferFormat.Xlsx
            ? Math.Min(Settings.Limits.MaxExportRows, TabularWriter.MaxXlsxRows)
            : Settings.Limits.MaxExportRows;

        return UnitOfWork.RunAsync(async (services, ct) =>
        {
            var definition = services.GetRequiredService<TransferDefinitionRegistry>().GetExport(job.DefinitionKey);
            await writer.WriteRowAsync(Headers(definition, job.Culture), ct);

            long rows = 0;
            await foreach (var row in definition.StreamAsync(context, ct).WithCancellation(ct))
            {
                if (rows >= maxRows)
                {
                    throw new InvalidTransferFileException("too_many_rows", "Transfer.Export.too_many_rows", [maxRows]);
                }

                await writer.WriteRowAsync(row, ct);
                rows++;
                if (rows % Settings.ExportProgressEvery != 0)
                    continue;
                await ConsumeRowsAsync(services, Settings.ExportProgressEvery, ct);
                if (!await ReportProgressAsync(job.Id, owner, rows))
                    return -1;
            }

            await ConsumeRowsAsync(services, rows % Settings.ExportProgressEvery, ct);
            return rows;
        }, cancellationToken);
    }

    private Task<bool> ReportProgressAsync(Guid jobId, string owner, long rows) =>
        UpdateUnderLeaseAsync(jobId, owner, (job, _) =>
        {
            job.RecordExported(rows);
            return ContinueOrCancel(job);
        });

    private Task<IReadOnlyList<TransferColumnKind>> KindsOfAsync(TransferJob job) =>
        UnitOfWork.RunAsync((services, _) => Task.FromResult<IReadOnlyList<TransferColumnKind>>(
            services.GetRequiredService<TransferDefinitionRegistry>().GetExport(job.DefinitionKey).Columns
                .Select(c => c.Kind).ToList()), CancellationToken.None);

    internal static IReadOnlyList<string?> Headers(ITransferDefinition definition, string culture) =>
        definition.Columns.Select(c => (string?)c.HeaderFor(culture)).ToList();

    internal static IReadOnlyDictionary<string, string> ParseFilter(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
