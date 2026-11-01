using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetSubscriberSubscriptionStatusQuery(Guid SubscriberId)
    : IQuery<GetSubscriberSubscriptionStatusResult>;

public record GetSubscriberSubscriptionStatusResult(
    Guid SubscriberId,
    SubscriptionStatus Status,
    DateTime? GracePeriodEndsAt,
    DateTime? CancellationEffectiveDate,
    int MaxUsers,
    int MaxItems,
    int MaxStorageGb,
    Dictionary<string, bool> Features);
