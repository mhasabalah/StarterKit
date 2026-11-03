using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetPaymentById;

public class GetPaymentByIdHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPaymentByIdQuery, AdminPaymentDto?>
{
    public async Task<AdminPaymentDto?> Handle(
        GetPaymentByIdQuery query,
        CancellationToken cancellationToken)
    {
        return await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Id == query.PaymentId)
            .Select(p => new AdminPaymentDto(
                p.Id,
                p.SubscriptionId,
                p.Subscription.SubscriberId,
                p.Amount,
                p.Currency,
                p.Status.ToString(),
                new PaymentMethodSnapshotDto(
                    p.PaymentMethodId,
                    p.PaymentMethodType,
                    p.PaymentMethodDisplayName,
                    p.PaymentMethodValue,
                    p.PaymentMethodDescription),
                p.PaidAt,
                p.CreatedAt,
                p.ReviewedBy,
                p.ReviewedAt,
                p.Notes,
                p.Purpose.ToString(),
                p.Version))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
