using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.GetSubscriberSubscriptionStatus;

public class GetSubscriberSubscriptionStatusHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetSubscriberSubscriptionStatusQuery, GetSubscriberSubscriptionStatusResult>
{
    public async Task<GetSubscriberSubscriptionStatusResult> Handle(
        GetSubscriberSubscriptionStatusQuery query,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                x.Status,
                x.GracePeriodEndsAt,
                x.ScheduledCancellationDate,
                x.EndDate,
                x.MaxUsers,
                x.MaxItems,
                x.MaxStorageGb,
                x.FeaturesJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return new GetSubscriberSubscriptionStatusResult(
                query.SubscriberId,
                SubscriptionStatus.Expired,
                null,
                null,
                0,
                0,
                0,
                []);
        }

        var cancellationEffectiveDate = subscription.ScheduledCancellationDate ??
            (subscription.Status == SubscriptionStatus.Cancelled ? subscription.EndDate : null);

        return new GetSubscriberSubscriptionStatusResult(
            query.SubscriberId,
            subscription.Status,
            subscription.GracePeriodEndsAt?.UtcDateTime,
            cancellationEffectiveDate?.UtcDateTime,
            subscription.MaxUsers ?? 0,
            subscription.MaxItems ?? 0,
            subscription.MaxStorageGb ?? 0,
            FeaturesJson.Deserialize(subscription.FeaturesJson)
                .ToDictionary(x => x.Key, x => bool.TryParse(x.Value, out var enabled) && enabled));
    }
}
