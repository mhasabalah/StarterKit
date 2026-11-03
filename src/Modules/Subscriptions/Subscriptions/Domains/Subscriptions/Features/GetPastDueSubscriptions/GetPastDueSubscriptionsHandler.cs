using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetPastDueSubscriptions;

public class GetPastDueSubscriptionsQueryValidator : AbstractValidator<GetPastDueSubscriptionsQuery>
{
    public GetPastDueSubscriptionsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetPastDueSubscriptionsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPastDueSubscriptionsQuery, PaginatedResult<PastDueSubscriptionDto>>
{
    public async Task<PaginatedResult<PastDueSubscriptionDto>> Handle(
        GetPastDueSubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.PastDue);

        queryable = queryable.OrderBy(x => x.GracePeriodEndsAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(x => new PastDueSubscriptionDto(
            x.Id,
            x.SubscriberId,
            x.PlanName,
            x.GracePeriodEndsAt,
            x.GracePeriodEndsAt.HasValue
                ? (x.GracePeriodEndsAt.Value.UtcDateTime - DateTime.UtcNow).Days
                : 0,
            x.Payments
                .Where(p => p.Purpose == PaymentPurpose.Renewal)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Status.ToString())
                .FirstOrDefault() ?? PaymentStatus.Pending.ToString(),
            x.Payments
                .Where(p => p.Purpose == PaymentPurpose.Renewal)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefault(),
            x.Payments
                .Where(p => p.Purpose == PaymentPurpose.Renewal)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => (decimal?)p.Amount)
                .FirstOrDefault(),
            x.Version));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
