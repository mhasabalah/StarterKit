using Attachment.Contracts.Attachments.Commands;
using Hangfire;

namespace Attachment.Infrastructure.BackgroundJobs;

public class CleanupExpiredPendingAttachmentsJob
{
    private readonly ISender _sender;
    private readonly ILogger<CleanupExpiredPendingAttachmentsJob> _logger;

    public CleanupExpiredPendingAttachmentsJob(
        ISender sender,
        ILogger<CleanupExpiredPendingAttachmentsJob> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("Starting cleanup of expired pending attachments");

        try
        {
            var command = new CleanupExpiredPendingAttachmentsCommand(
                DateTime.UtcNow,
                BatchSize: 100);

            var result = await _sender.Send(command);

            _logger.LogInformation(
                "Cleanup completed: {Cleaned} pending attachments cleaned, {FilesDeleted} files deleted",
                result.Cleaned,
                result.FilesDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of expired pending attachments");
            throw;
        }
    }
}
