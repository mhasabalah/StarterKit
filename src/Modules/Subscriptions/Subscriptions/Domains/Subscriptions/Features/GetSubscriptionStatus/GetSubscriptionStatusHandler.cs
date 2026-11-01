using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetSubscriptionStatus;

public class GetSubscriptionStatusHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetSubscriptionStatusQuery, SubscriptionStatusDto?>
{
    public async Task<SubscriptionStatusDto?> Handle(
        GetSubscriptionStatusQuery query,
        CancellationToken cancellationToken)
    {
        var sub = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId &&
                        (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue))
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                x.Status,
                x.PlanName,
                x.RenewalDate,
                x.GracePeriodEndsAt,
                x.Price,
                x.Currency,
                x.PlanId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null)
            return null;

        var plan = await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.Id == sub.PlanId)
            .Select(p => new { p.PendingPrice, p.PriceEffectiveAt })
            .FirstOrDefaultAsync(cancellationToken);

        return new SubscriptionStatusDto(
            sub.Status.ToString(),
            sub.PlanName,
            sub.RenewalDate,
            sub.GracePeriodEndsAt,
            sub.Price,
            sub.Currency,
            plan?.PendingPrice,
            plan?.PriceEffectiveAt);
    }
}
