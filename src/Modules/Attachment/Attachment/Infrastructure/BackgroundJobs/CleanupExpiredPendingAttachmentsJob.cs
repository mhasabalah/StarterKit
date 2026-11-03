using Shared.Core.MultiTenancy.BackgroundJobs;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Infrastructure.Configuration;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>Recurring: removes expired pending uploads, once per tenant (<see cref="ITenantJobRunner"/>).</summary>
public class CleanupExpiredPendingAttachmentsJob(
    ISender sender,
    ITenantJobRunner tenantJobs,
    IOptions<AttachmentOptions> options,
    ILogger<CleanupExpiredPendingAttachmentsJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task ExecuteAsync() =>
        tenantJobs.ForEachTenantScopeAsync(async (tenant, ct) =>
        {
            var result = await sender.Send(new CleanupExpiredPendingAttachmentsCommand(
                DateTime.UtcNow, options.Value.Pending.CleanupBatchSize), ct);

            logger.LogInformation("Pending cleanup job ({Tenant}): {Cleaned} cleaned, {Files} files deleted",
                tenant.Identifier, result.Cleaned, result.FilesDeleted);
        }, nameof(CleanupExpiredPendingAttachmentsJob));
}
