using Subscriptions.Domains.Subscriptions.Services;
namespace Subscriptions.Domains.Subscriptions.Features.GetMySubscriptionInvoices;

public class GetMySubscriptionInvoicesQueryValidator : AbstractValidator<GetMySubscriptionInvoicesQuery>
{
    public GetMySubscriptionInvoicesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetMySubscriptionInvoicesHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetMySubscriptionInvoicesQuery, PaginatedResult<InvoiceDto>>
{
    public async Task<PaginatedResult<InvoiceDto>> Handle(
        GetMySubscriptionInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        // All of the subscriber's invoices and credit notes (snapshot carries the subscriber id), so documents of a
        // cancelled subscription (e.g. refund credit notes) stay visible.
        var subscriberId = query.SubscriberId ?? Guid.Empty;
        var invoiceQueryable = dbContext.Invoices
            .AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId)
            .OrderByDescending(x => x.IssuedAt);

        var invoiceQueryable2 = invoiceQueryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = invoiceQueryable2.Select(InvoiceProjections.ToInvoiceDto);

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
