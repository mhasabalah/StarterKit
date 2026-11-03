using MassTransit;
using Microsoft.Extensions.Options;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Tenancy.Services;

/// <summary>
/// Multi-tenant billing: the subscriber is the tenant. An expired subscription suspends the tenant; an active one lifts
/// a BILLING suspension (never a suspension an operator made for another reason). No-op for subscribers that are not
/// tenants (single-tenant mode: users) and for the Default tenant.
/// <para>
/// Integration-event consumer (at-least-once, possibly out of order): the event is only a trigger. The decision is
/// made from the subscriber's CURRENT status (<see cref="GetSubscriberSubscriptionStatusQuery"/>), so duplicates are
/// no-ops and an old "expired" delivered after a newer "activated" cannot suspend a paying tenant.
/// </para>
/// Runs outside MediatR, so the tenant's domain events (cache invalidation, audit...) are dispatched explicitly after
/// the save.
/// </summary>
public sealed class TenantBillingStatusConsumer(
    TenancyDbContext dbContext,
    ISender sender,
    IDomainEventDispatcher dispatcher,
    IOptions<MultiTenancyOptions> options,
    ILogger<TenantBillingStatusConsumer> logger)
    : IConsumer<SubscriptionActivatedIntegrationEvent>, IConsumer<SubscriptionExpiredIntegrationEvent>
{
    public const string BillingSuspensionReason = "Subscription expired";

    public Task Consume(ConsumeContext<SubscriptionActivatedIntegrationEvent> context) =>
        ReconcileAsync(context.Message.SubscriberId, context.Message.SubscriptionId, context.CancellationToken);

    public Task Consume(ConsumeContext<SubscriptionExpiredIntegrationEvent> context) =>
        ReconcileAsync(context.Message.SubscriberId, context.Message.SubscriptionId, context.CancellationToken);

    private async Task ReconcileAsync(Guid subscriberId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || subscriberId == TenantDefaults.DefaultTenantId) return;

        using (TenantScope.BeginHostScope())
        {
            var tenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == subscriberId, cancellationToken);
            if (tenant is null) return; // the subscriber is not a tenant

            var status = (await sender.Send(new GetSubscriberSubscriptionStatusQuery(subscriberId), cancellationToken)).Status;

            switch (status)
            {
                case SubscriptionStatus.Expired when tenant.EffectiveStatus == TenantStatus.Active:
                    tenant.Suspend(BillingSuspensionReason);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogWarning("Tenant {Tenant} suspended: subscription {SubscriptionId} expired",
                        tenant.Identifier, subscriptionId);
                    break;

                case SubscriptionStatus.Active when tenant is
                    { EffectiveStatus: TenantStatus.Suspended, StatusReason: BillingSuspensionReason }:
                    tenant.Reactivate();
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Tenant {Tenant} reactivated: subscription {SubscriptionId} active",
                        tenant.Identifier, subscriptionId);
                    break;

                default:
                    return; // already in the right state (duplicate / out-of-order delivery) or nothing to do
            }

            await dispatcher.DispatchPendingAsync(cancellationToken);
        }
    }
}
