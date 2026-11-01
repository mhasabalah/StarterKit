using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.Subscriptions.Services;
using ContractSubscriptionStatus = Subscriptions.Contracts.Subscriptions.Enums.SubscriptionStatus;
using DomainSubscriptionStatus = Subscriptions.Domains.Subscriptions.Models.SubscriptionStatus;

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
                ContractSubscriptionStatus.Expired,
                null,
                null,
                0,
                0,
                0,
                []);
        }

        var cancellationEffectiveDate = subscription.ScheduledCancellationDate ??
            (subscription.Status == DomainSubscriptionStatus.Cancelled ? subscription.EndDate : null);

        return new GetSubscriberSubscriptionStatusResult(
            query.SubscriberId,
            ToContractStatus(subscription.Status),
            subscription.GracePeriodEndsAt?.UtcDateTime,
            cancellationEffectiveDate?.UtcDateTime,
            subscription.MaxUsers ?? 0,
            subscription.MaxItems ?? 0,
            subscription.MaxStorageGb ?? 0,
            SubscriptionCacheSerializer.ExtractFeatures(subscription.FeaturesJson)
                .ToDictionary(x => x.Key, x => bool.TryParse(x.Value, out var enabled) && enabled));
    }

    private static ContractSubscriptionStatus ToContractStatus(DomainSubscriptionStatus status)
        => status switch
        {
            DomainSubscriptionStatus.Active => ContractSubscriptionStatus.Active,
            DomainSubscriptionStatus.PastDue => ContractSubscriptionStatus.PastDue,
            DomainSubscriptionStatus.Cancelled => ContractSubscriptionStatus.Cancelled,
            _ => ContractSubscriptionStatus.Expired
        };
}
