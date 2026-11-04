using Attachment.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.BackgroundJobs;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Attachment.Domains.Attachments.Consumers;

/// <summary>
/// Account erasure: deletes the user's PERSONAL files (the avatar: <c>EntityType.User</c> with the user as entity) in
/// EVERY tenant. Tenant-owned files the user merely uploaded (product images, invoices, payment proofs) are business
/// records of the tenant and stay: the user row is anonymized, so their <c>UploadedBy</c> is pseudonymous. Per tenant, in its own
/// scope and unit of work, the rows are hidden (<c>PendingDeletion</c>, same path as a normal delete) and
/// <see cref="DeleteAttachmentFilesJob"/> removes blobs and rows; the reconciliation sweeper backs it up.
/// <para>
/// Idempotent: rows already pending deletion are skipped, so a re-delivery finds nothing. If any tenant fails the
/// consumer throws after visiting all of them, so the bus retries (the finished tenants are no-ops then). Tenants
/// that are <c>Migrating</c> are skipped by the fan-out; since they may hold personal files too, the consumer throws
/// (after processing the others) until they are serviceable again, so the erasure is never silently incomplete.
/// </para>
/// </summary>
public sealed class UserDeletedAttachmentConsumer(
    ITenantJobRunner tenantRunner,
    ITenantStore tenantStore,
    IUnitOfWorkRunner runner,
    IAttachmentBackgroundJobs backgroundJobs,
    TimeProvider time,
    ILogger<UserDeletedAttachmentConsumer> logger) : IConsumer<UserDeletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<UserDeletedIntegrationEvent> context)
    {
        var userId = context.Message.UserId;

        var failedTenants = await tenantRunner.ForEachTenantScopeAsync(async (tenant, ct) =>
        {
            var now = time.GetUtcNow().UtcDateTime;

            var ids = await runner.RunAsync(async (services, token) =>
            {
                var repository = services.GetRequiredService<IAttachmentRepository>();
                var files = await repository.GetUploadedByForErasureAsync(userId, token);
                foreach (var file in files)
                    file.RequestDeletion(now);
                if (files.Count > 0)
                    await repository.SaveChangesAsync(token);
                return files.Select(f => f.Id).ToList();
            }, ct);

            if (ids.Count == 0)
                return;

            backgroundJobs.EnqueueFileDeletion(ids);
            logger.LogInformation("Erasure of user {UserId}: {Count} file(s) of tenant {Tenant} queued for deletion",
                userId, ids.Count, tenant.Identifier);
        }, nameof(UserDeletedAttachmentConsumer), context.CancellationToken);

        var migrating = await tenantStore.GetMigratingTenantsAsync(context.CancellationToken);
        if (migrating.Count > 0)
        {
            throw new InvalidOperationException(
                $"Erasure of user {userId}: {migrating.Count} tenant(s) are migrating and were skipped; will retry.");
        }

        if (failedTenants > 0)
        {
            throw new InvalidOperationException(
                $"Erasure of user {userId}: files could not be queued for deletion in {failedTenants} tenant(s); will retry.");
        }
    }
}
