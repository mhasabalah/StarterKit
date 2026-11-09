using Subscriptions.Contracts.Subscriptions.Events;
using Subscriptions.Domains.Ledger.Events;
using Subscriptions.Domains.Ledger.Services;

namespace Subscriptions.Domains.Ledger.EventHandlers;

/// <summary>
/// Drops every cached billing dashboard when one of its figures can change:
/// <list type="bullet">
/// <item>revenue: any ledger entry. <see cref="LedgerEntryRecordedEvent"/> covers payments, refunds, credit notes,
/// provider refunds and comps, because <c>ILedgerRepository</c> is the only ledger write;</item>
/// <item>the payment review queue: submitted, approved, rejected;</item>
/// <item>MRR, status counts and churn: every subscription status or price change.</item>
/// </list>
/// Domain events are dispatched after the commit, so the next read computes from committed data.
/// </summary>
public sealed class BillingDashboardCacheInvalidationHandler(IBillingDashboardCache cache) :
    INotificationHandler<LedgerEntryRecordedEvent>,
    INotificationHandler<PaymentSubmittedEvent>,
    INotificationHandler<PaymentApprovedEvent>,
    INotificationHandler<PaymentRejectedEvent>,
    INotificationHandler<SubscriptionCreatedEvent>,
    INotificationHandler<SubscriptionActivatedEvent>,
    INotificationHandler<SubscriptionRejectedEvent>,
    INotificationHandler<SubscriptionCancelledEvent>,
    INotificationHandler<SubscriptionExpiredEvent>,
    INotificationHandler<SubscriptionPastDueEvent>,
    INotificationHandler<SubscriptionRenewedEvent>,
    INotificationHandler<SubscriptionUpgradedEvent>,
    INotificationHandler<SubscriptionUpgradeRevertedEvent>,
    INotificationHandler<SubscriptionDowngradeAppliedEvent>
{
    public Task Handle(LedgerEntryRecordedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(PaymentSubmittedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(PaymentApprovedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(PaymentRejectedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionCreatedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionActivatedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionRejectedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionCancelledEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionExpiredEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionPastDueEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionRenewedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionUpgradedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionUpgradeRevertedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
    public Task Handle(SubscriptionDowngradeAppliedEvent e, CancellationToken ct) => cache.InvalidateAsync(ct);
}
