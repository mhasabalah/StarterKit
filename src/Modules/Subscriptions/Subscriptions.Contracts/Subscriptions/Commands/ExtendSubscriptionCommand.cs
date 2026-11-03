namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Admin grants extra days. Active: the renewal date moves (with a scheduled cancellation / pending downgrade tied
/// to it). PastDue: the grace period is extended. Audited and written to the transition log.
/// </summary>
/// <param name="Days">1-366.</param>
/// <param name="Version">The subscription version the admin saw (stale = 409).</param>
public record ExtendSubscriptionCommand(
    Guid SubscriptionId,
    Guid AdminId,
    int Days,
    string Reason,
    long? Version = null) : ICommand<ExtendSubscriptionResult>;

public record ExtendSubscriptionResult(
    Guid SubscriptionId,
    string Status,
    DateTimeOffset? RenewalDate,
    DateTimeOffset? GracePeriodEndsAt);
