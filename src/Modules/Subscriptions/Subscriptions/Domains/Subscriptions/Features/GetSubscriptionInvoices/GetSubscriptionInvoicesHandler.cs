using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetSubscriptionInvoices;

public class GetSubscriptionInvoicesQueryValidator : AbstractValidator<GetSubscriptionInvoicesQuery>
{
    public GetSubscriptionInvoicesQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetSubscriptionInvoicesHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetSubscriptionInvoicesQuery, PaginatedResult<InvoiceDto>>
{
    public async Task<PaginatedResult<InvoiceDto>> Handle(
        GetSubscriptionInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        var ownerCheck = await dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(x => x.Id == query.SubscriptionId && x.SubscriberId == query.SubscriberId,
                cancellationToken);

        if (!ownerCheck)
            throw new ForbiddenException("You are not allowed to view invoices for this subscription.");

        var queryable = dbContext.Invoices
            .AsNoTracking()
            .Where(x => x.SubscriptionId == query.SubscriptionId);

        queryable = queryable.OrderByDescending(x => x.IssuedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(InvoiceProjections.ToInvoiceDto);

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
