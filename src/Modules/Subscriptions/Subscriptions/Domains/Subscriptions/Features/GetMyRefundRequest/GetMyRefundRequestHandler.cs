using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetMyRefundRequest;

public class GetMyRefundRequestHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetMyRefundRequestQuery, RefundRequestDto?>
{
    public async Task<RefundRequestDto?> Handle(GetMyRefundRequestQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.RefundRequests
            .AsNoTracking()
            .Where(x => x.SubscriptionId == query.SubscriptionId && x.SubscriberId == query.SubscriberId)
            .OrderByDescending(x => x.RequestedAt)
            .Select(x => new RefundRequestDto(
                x.Id,
                x.SubscriptionId,
                x.PlanName,
                x.RequestedAmount,
                x.Currency,
                x.RefundType.ToString(),
                x.Status.ToString(),
                x.RequestedAt,
                x.ProcessedAt,
                x.ExternalTransactionRef))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
