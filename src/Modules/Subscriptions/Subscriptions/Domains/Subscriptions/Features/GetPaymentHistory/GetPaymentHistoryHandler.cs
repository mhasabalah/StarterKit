using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetPaymentHistory;

public class GetPaymentHistoryQueryValidator : AbstractValidator<GetPaymentHistoryQuery>
{
    public GetPaymentHistoryQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetPaymentHistoryHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPaymentHistoryQuery, PaginatedResult<PaymentHistoryDto>>
{
    public async Task<PaginatedResult<PaymentHistoryDto>> Handle(
        GetPaymentHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Subscription.SubscriberId == query.SubscriberId);

        queryable = queryable.OrderByDescending(p => p.CreatedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(p => new PaymentHistoryDto(
            p.Id,
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
            p.CreatedAt));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
