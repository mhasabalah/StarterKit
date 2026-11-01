using Shared.Contracts.Pagination;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetAdminRefundRequests;

public class GetAdminRefundRequestsQueryValidator : AbstractValidator<GetAdminRefundRequestsQuery>
{
    public GetAdminRefundRequestsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetAdminRefundRequestsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetAdminRefundRequestsQuery, PaginatedResult<AdminRefundRequestDto>>
{
    public async Task<PaginatedResult<AdminRefundRequestDto>> Handle(
        GetAdminRefundRequestsQuery query, CancellationToken cancellationToken)
    {
        var queryable = dbContext.RefundRequests.AsNoTracking();

        if (query.Status.HasValue)
            queryable = queryable.Where(x => x.Status == query.Status.Value);

        queryable = queryable.OrderByDescending(x => x.RequestedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(x => new AdminRefundRequestDto(
            x.Id,
            x.SubscriptionId,
            x.SubscriberId,
            x.PlanName,
            x.BillingCycleInMonths,
            x.SubscriptionStartedAt,
            x.CancelledAt,
            x.RenewalDate,
            x.RequestedAmount,
            x.Currency,
            x.RefundType.ToString(),
            x.Status.ToString(),
            x.RequestedAt,
            x.ReviewedBy,
            x.ReviewedAt,
            x.ReviewNotes,
            x.ProcessedAt,
            x.ExternalTransactionRef));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
