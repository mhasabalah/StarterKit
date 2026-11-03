using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetPendingSubscriptions;

public class GetPendingSubscriptionsQueryValidator : AbstractValidator<GetPendingSubscriptionsQuery>
{
    public GetPendingSubscriptionsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetPendingSubscriptionsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPendingSubscriptionsQuery, PaginatedResult<PendingSubscriptionDto>>
{
    public async Task<PaginatedResult<PendingSubscriptionDto>> Handle(
        GetPendingSubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.PendingApproval && x.CreatedAt.HasValue);

        queryable = queryable.OrderByDescending(x => x.CreatedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(x => new PendingSubscriptionDto(
            x.Id,
            x.SubscriberId,
            x.PlanName,
            x.Price,
            x.Currency,
            x.CreatedAt!.Value,
            x.Payments
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Status.ToString())
                .FirstOrDefault() ?? PaymentStatus.Pending.ToString(),
            x.Payments
                .Where(p => p.Purpose == PaymentPurpose.Activation)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefault(),
            x.Version));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
