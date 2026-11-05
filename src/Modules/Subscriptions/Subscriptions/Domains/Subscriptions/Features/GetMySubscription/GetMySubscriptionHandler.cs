using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetMySubscription;

public class GetMySubscriptionHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetMySubscriptionQuery, SubscriptionDto?>
{
    public async Task<SubscriptionDto?> Handle(GetMySubscriptionQuery query, CancellationToken cancellationToken)
    {
        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId &&
                        x.Status != SubscriptionStatus.Cancelled &&
                        x.Status != SubscriptionStatus.Expired)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                Subscription = x,
                LatestPayment = x.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault(),
                ScheduledChange = x.ScheduledPlanChanges.FirstOrDefault(c => c.Status == PlanChangeStatus.Pending)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
            return null;

        var plan = await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.Id == subscription.Subscription.PlanId)
            .Select(p => new { p.PendingPrice, p.PriceEffectiveAt })
            .FirstOrDefaultAsync(cancellationToken);

        var latestPayment = subscription.LatestPayment;
        var latestPaymentDto = latestPayment is null
            ? null
            : new SubscriptionPaymentDto(
                latestPayment.Id,
                latestPayment.Status.ToString(),
                latestPayment.Amount,
                latestPayment.Currency,
                new PaymentMethodSnapshotDto(
                    latestPayment.PaymentMethodId,
                    latestPayment.PaymentMethodType,
                    latestPayment.PaymentMethodDisplayName,
                    latestPayment.PaymentMethodValue,
                    latestPayment.PaymentMethodDescription),
                latestPayment.PaidAt);

        ScheduledPlanChangeDto? scheduledChangeDto = null;
        if (subscription.ScheduledChange is { Status: PlanChangeStatus.Pending } change)
        {
            scheduledChangeDto = new ScheduledPlanChangeDto(
                change.NewPlanName,
                change.NewPrice,
                subscription.Subscription.Currency,
                change.EffectiveAt);
        }

        return new SubscriptionDto(
            subscription.Subscription.Id,
            subscription.Subscription.PlanName,
            subscription.Subscription.Status.ToString(),
            subscription.Subscription.Price,
            subscription.Subscription.Currency,
            subscription.Subscription.BillingCycleInMonths,
            subscription.Subscription.MaxItems,
            subscription.Subscription.MaxStorageGb,
            subscription.Subscription.MaxUsers,
            subscription.Subscription.StartDate,
            subscription.Subscription.RenewalDate,
            subscription.Subscription.GracePeriodEndsAt,
            latestPaymentDto,
            scheduledChangeDto,
            subscription.Subscription.CancelledAtPeriodEnd,
            subscription.Subscription.ScheduledCancellationDate,
            subscription.Subscription.CancellationReason?.ToString(),
            plan?.PendingPrice,
            plan?.PriceEffectiveAt,
            subscription.Subscription.CurrentPeriodStart,
            subscription.Subscription.DeferredProrationAmount,
            subscription.Subscription.Version,
            subscription.Subscription.TrialEndsAt,
            subscription.Subscription.CouponCode);
    }
}
