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
        var subscriberId = query.SubscriberId ?? Guid.Empty;
        var subscriptionId = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId &&
                        x.Status != SubscriptionStatus.Cancelled &&
                        x.Status != SubscriptionStatus.Expired)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscriptionId is null)
            return new PaginatedResult<InvoiceDto>
            {
                Result = [],
                PageInfoResult = new PageInfoResult
                {
                    TotalItems = 0,
                    CurrentPage = query.PageNumber ?? 1,
                    PageSize = query.PageSize ?? 10
                }
            };

        var invoiceQueryable = dbContext.Invoices
            .AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId.Value)
            .OrderByDescending(x => x.IssuedAt);

        var invoiceQueryable2 = invoiceQueryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = invoiceQueryable2.Select(x => new InvoiceDto(
                x.Id,
                x.InvoiceNumber,
                x.Type.ToString(),
                x.Status.ToString(),
                x.Amount,
                x.TaxRate,
                Math.Round(x.Amount * (1 - x.TaxRate), 2),
                Math.Round(x.Amount * x.TaxRate, 2),
                x.Currency,
                x.IsDeferred,
                x.IsVoided,
                x.IssuedAt));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
