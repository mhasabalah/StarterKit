using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetRenewalStatus;

public class GetRenewalStatusHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetRenewalStatusQuery, RenewalStatusDto?>
{
    public async Task<RenewalStatusDto?> Handle(
        GetRenewalStatusQuery query,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId && x.Status == SubscriptionStatus.PastDue)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.GracePeriodEndsAt,
                x.PlanId,
                x.Price,
                x.Currency,
                LatestPayment = x.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription?.LatestPayment is null)
            return null;

        var plan = await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.Id == subscription.PlanId)
            .Select(p => new { p.Price, p.PendingPrice, p.PriceEffectiveAt })
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        decimal expectedAmount;
        if (plan is { PendingPrice: not null, PriceEffectiveAt: not null } && plan.PriceEffectiveAt <= now)
            expectedAmount = plan.PendingPrice.Value;   // effective but job hasn't promoted yet
        else if (plan != null && plan.Price != subscription.Price)
            expectedAmount = plan.Price;                // already promoted by ProcessPlanPriceChangesJob
        else
            expectedAmount = subscription.Price;

        var daysUntilExpiry = subscription.GracePeriodEndsAt.HasValue
            ? (subscription.GracePeriodEndsAt.Value.UtcDateTime - DateTime.UtcNow).Days
            : 0;

        return new RenewalStatusDto(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.GracePeriodEndsAt,
            daysUntilExpiry,
            subscription.LatestPayment.Id,
            subscription.LatestPayment.Status.ToString(),
            subscription.LatestPayment.Amount,
            subscription.Currency,
            expectedAmount);
    }
}
