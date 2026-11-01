namespace Subscriptions.Domains.Subscriptions.Models;

public class SubscriptionTransitionLog
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public SubscriptionStatus? FromStatus { get; private set; }
    public SubscriptionStatus ToStatus { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorType { get; private set; } = default!;
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private SubscriptionTransitionLog()
    {
    }

    public static SubscriptionTransitionLog Create(
        Guid subscriptionId,
        Guid subscriberId,
        SubscriptionStatus? fromStatus,
        SubscriptionStatus toStatus,
        Guid actorId,
        string actorType,
        string? reason)
    {
        return new SubscriptionTransitionLog
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            SubscriberId = subscriberId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorId = actorId,
            ActorType = actorType,
            Reason = reason,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
