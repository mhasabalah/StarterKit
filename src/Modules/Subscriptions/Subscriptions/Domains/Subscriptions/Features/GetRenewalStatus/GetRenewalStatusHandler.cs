using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetRenewalStatus;

/// <summary>
/// The open renewal for a past-due subscriber. The expected amount is the amount on the renewal PAYMENT (priced at
/// the boundary, incl. deferred proration), not the live plan price.
/// </summary>
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
                x.Currency,
                RenewalPayment = x.Payments
                    .Where(p => p.Purpose == PaymentPurpose.Renewal)
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription?.RenewalPayment is null)
            return null;

        var daysUntilExpiry = subscription.GracePeriodEndsAt.HasValue
            ? (subscription.GracePeriodEndsAt.Value.UtcDateTime - DateTime.UtcNow).Days
            : 0;

        return new RenewalStatusDto(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.GracePeriodEndsAt,
            daysUntilExpiry,
            subscription.RenewalPayment.Id,
            subscription.RenewalPayment.Status.ToString(),
            subscription.RenewalPayment.Amount,
            subscription.Currency,
            subscription.RenewalPayment.Amount);
    }
}
