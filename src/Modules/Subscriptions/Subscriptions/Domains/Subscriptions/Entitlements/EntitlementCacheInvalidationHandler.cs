using Subscriptions.Contracts.Subscriptions.Events;

namespace Subscriptions.Domains.Subscriptions.Entitlements;

/// <summary>
/// Drops the cached entitlements of a subscriber whenever its subscription status, plan or features change.
/// Runs after the committed save (domain events are dispatched post-commit), so the next read sees the new state.
/// </summary>
public sealed class EntitlementCacheInvalidationHandler(IEntitlementCache cache) :
    INotificationHandler<SubscriptionActivatedEvent>,
    INotificationHandler<SubscriptionCancelledEvent>,
    INotificationHandler<SubscriptionExpiredEvent>,
    INotificationHandler<SubscriptionPastDueEvent>,
    INotificationHandler<SubscriptionRejectedEvent>,
    INotificationHandler<SubscriptionRenewedEvent>,
    INotificationHandler<SubscriptionUpgradedEvent>,
    INotificationHandler<SubscriptionUpgradeRevertedEvent>,
    INotificationHandler<SubscriptionDowngradeAppliedEvent>
{
    public Task Handle(SubscriptionActivatedEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionCancelledEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionExpiredEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionPastDueEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionRejectedEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionRenewedEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionUpgradedEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionUpgradeRevertedEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
    public Task Handle(SubscriptionDowngradeAppliedEvent e, CancellationToken ct) => cache.InvalidateAsync(e.SubscriberId, ct);
}
