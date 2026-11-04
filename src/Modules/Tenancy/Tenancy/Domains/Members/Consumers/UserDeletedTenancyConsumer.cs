using MassTransit;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Tenancy.Domains.Members.Consumers;

/// <summary>
/// Account erasure: removes the user's membership in every tenant (the membership is personal data - roles and
/// branches of a named person). Idempotent: a re-delivery finds no membership and does nothing. Runs in host scope
/// (the event is not tenant-bound) and dispatches the membership domain events explicitly, so permission caches
/// are invalidated.
/// <para>
/// Owners are not expected here - the deletion command refuses while the user owns a tenant. If ownership was
/// acquired in the meantime the membership is still removed (the account is gone) and a warning flags the tenant
/// for an operator, who can assign a new owner.
/// </para>
/// </summary>
public sealed class UserDeletedTenancyConsumer(
    TenancyDbContext dbContext,
    IDomainEventDispatcher dispatcher,
    ILogger<UserDeletedTenancyConsumer> logger) : IConsumer<UserDeletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<UserDeletedIntegrationEvent> context)
    {
        var userId = context.Message.UserId;

        using (TenantScope.BeginHostScope())
        {
            var memberships = await dbContext.Memberships
                .Where(m => m.UserId == userId)
                .ToListAsync(context.CancellationToken);
            if (memberships.Count == 0)
                return;

            foreach (var membership in memberships)
            {
                if (membership.IsOwner)
                {
                    logger.LogWarning(
                        "Deleted user {UserId} still owned tenant {TenantId}; the tenant needs a new owner",
                        userId, membership.TenantId);
                    membership.RevokeOwnership();
                }

                membership.Remove();
                dbContext.Memberships.Remove(membership);
            }

            await dbContext.SaveChangesAsync(context.CancellationToken);
            await dispatcher.DispatchPendingAsync(context.CancellationToken);

            logger.LogInformation("Removed {Count} membership(s) of deleted user {UserId}", memberships.Count, userId);
        }
    }
}
