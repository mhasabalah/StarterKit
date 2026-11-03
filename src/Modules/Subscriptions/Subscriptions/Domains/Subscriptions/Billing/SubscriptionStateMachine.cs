namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>
/// The allowed subscription status transitions, in one table (the entity methods consult it).
/// <code>
/// PendingApproval → Active | Rejected
/// Active          → PastDue | Cancelled
/// PastDue         → Active (renewed) | Expired | Cancelled
/// Expired, Cancelled, Rejected: terminal
/// </code>
/// Exception: an ADMIN may reopen a recently Cancelled/Expired subscription (<see cref="CanReactivate"/>): it goes
/// back to PendingApproval (new activation payment) or straight to Active through a complimentary activation.
/// That reopening is not a normal transition and is not part of the table.
/// </summary>
public static class SubscriptionStateMachine
{
    /// <summary>Statuses that count as the subscriber's one open subscription (filtered unique index).</summary>
    public static readonly IReadOnlyList<SubscriptionStatus> OpenStatuses =
        [SubscriptionStatus.PendingApproval, SubscriptionStatus.Active, SubscriptionStatus.PastDue];

    private static readonly Dictionary<SubscriptionStatus, SubscriptionStatus[]> Allowed = new()
    {
        [SubscriptionStatus.PendingApproval] = [SubscriptionStatus.Active, SubscriptionStatus.Rejected],
        [SubscriptionStatus.Active] = [SubscriptionStatus.PastDue, SubscriptionStatus.Cancelled],
        [SubscriptionStatus.PastDue] = [SubscriptionStatus.Active, SubscriptionStatus.Expired, SubscriptionStatus.Cancelled],
        [SubscriptionStatus.Expired] = [],
        [SubscriptionStatus.Cancelled] = [],
        [SubscriptionStatus.Rejected] = []
    };

    public static bool CanTransition(SubscriptionStatus from, SubscriptionStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureCanTransition(SubscriptionStatus from, SubscriptionStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidSubscriptionTransitionException(from, to);
        }
    }

    public static bool IsOpen(SubscriptionStatus status) => OpenStatuses.Contains(status);

    /// <summary>Statuses an admin may reopen (reactivate / comp).</summary>
    public static bool CanReactivate(SubscriptionStatus status) =>
        status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired;
}
