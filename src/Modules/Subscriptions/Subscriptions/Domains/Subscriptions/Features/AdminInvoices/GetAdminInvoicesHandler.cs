using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;
using Shared.Contracts.Pagination;

namespace Subscriptions.Domains.Subscriptions.Features.AdminInvoices;

public class GetAdminInvoicesQueryValidator : AbstractValidator<GetAdminInvoicesQuery>
{
    public GetAdminInvoicesQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetAdminInvoicesHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetAdminInvoicesQuery, PaginatedResult<AdminInvoiceDto>>
{
    public async Task<PaginatedResult<AdminInvoiceDto>> Handle(
        GetAdminInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Invoices.AsNoTracking();

        if (query.SubscriberId.HasValue)
            queryable = queryable.Where(i => i.SubscriberId == query.SubscriberId.Value);

        if (query.Status.HasValue)
            queryable = queryable.Where(i => (int)i.Status == query.Status.Value);

        if (query.Type.HasValue)
            queryable = queryable.Where(i => (int)i.Type == query.Type.Value);

        queryable = queryable.OrderByDescending(i => i.IssuedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        // Issue-time snapshot only: amounts are never recomputed from the current tax configuration.
        var dtoQuery = queryable.Select(Services.AdminBillingProjections.ToAdminInvoiceDto);

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
