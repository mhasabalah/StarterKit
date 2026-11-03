using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Maintenance;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>
/// Second half of an attachment delete: the rows were already hidden (<c>PendingDeletion</c>) by the business
/// command; this job deletes their blobs and then the rows. One unit of work per attachment
/// (<see cref="IUnitOfWorkRunner"/>), so one conflict or failure never affects the others.
/// <para>
/// Idempotent: a re-run (Hangfire retry, or the sweeper got there first) finds no row and does nothing. When a
/// blob could not be deleted the job throws after processing every id, so Hangfire retries it; beyond that the
/// reconciliation sweeper keeps retrying with backoff.
/// </para>
/// </summary>
public class DeleteAttachmentFilesJob(IUnitOfWorkRunner runner, ILogger<DeleteAttachmentFilesJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(Guid[] attachmentIds, CancellationToken cancellationToken)
    {
        var failed = new List<Guid>();

        foreach (var id in attachmentIds.Distinct())
        {
            try
            {
                var outcome = await runner.RunAsync(
                    (services, ct) => services.GetRequiredService<AttachmentFileReaper>().ReapAsync(id, ct),
                    cancellationToken);

                if (outcome == ReapOutcome.Failed)
                    failed.Add(id);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                // Someone else (the sweeper, a parallel retry) changed or removed the row: their run wins.
                logger.LogInformation("Attachment {AttachmentId} was changed concurrently; skipped", id);
            }
        }

        if (failed.Count > 0)
        {
            throw new FileStorageException(
                $"Files of {failed.Count} attachment(s) could not be deleted: {string.Join(", ", failed)}");
        }
    }
}
