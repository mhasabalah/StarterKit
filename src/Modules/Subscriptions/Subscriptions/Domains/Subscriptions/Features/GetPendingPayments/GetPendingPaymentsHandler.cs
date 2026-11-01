namespace Subscriptions.Domains.Subscriptions.Features.GetPendingPayments;

public class GetPendingPaymentsQueryValidator : AbstractValidator<GetPendingPaymentsQuery>
{
    public GetPendingPaymentsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetPendingPaymentsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPendingPaymentsQuery, PaginatedResult<PendingPaymentDto>>
{
    public async Task<PaginatedResult<PendingPaymentDto>> Handle(
        GetPendingPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Submitted);

        queryable = queryable.OrderBy(p => p.CreatedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(p => new PendingPaymentDto(
            p.Id,
            p.SubscriptionId,
            p.Subscription.SubscriberId,
            p.Amount,
            p.Currency,
            new PaymentMethodSnapshotDto(
                p.PaymentMethodId,
                p.PaymentMethodType,
                p.PaymentMethodDisplayName,
                p.PaymentMethodValue,
                p.PaymentMethodDescription),
            p.CreatedAt,
            "use /proof endpoint"));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
