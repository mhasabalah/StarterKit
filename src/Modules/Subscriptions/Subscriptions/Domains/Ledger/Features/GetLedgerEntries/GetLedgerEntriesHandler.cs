using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Ledger.Features.GetLedgerEntries;

public class GetLedgerEntriesQueryValidator : AbstractValidator<GetLedgerEntriesQuery>
{
    public GetLedgerEntriesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.Currency).MaximumLength(3);
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From < x.To)
            .WithMessage("From must be before To.");
    }
}

/// <summary>Paged, filtered read of the immutable ledger (newest first by default).</summary>
public class GetLedgerEntriesHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetLedgerEntriesQuery, PaginatedResult<LedgerEntryDto>>
{
    public async Task<PaginatedResult<LedgerEntryDto>> Handle(GetLedgerEntriesQuery query, CancellationToken cancellationToken)
    {
        var queryable = dbContext.LedgerEntries.AsNoTracking();

        // timestamptz parameters must be UTC (offset 0) - normalize client-supplied offsets.
        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            queryable = queryable.Where(x => x.OccurredAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            queryable = queryable.Where(x => x.OccurredAt < toUtc);
        }

        if (query.Type.HasValue)
            queryable = queryable.Where(x => x.Type == query.Type.Value);

        if (query.SubscriberId.HasValue)
            queryable = queryable.Where(x => x.SubscriberId == query.SubscriberId.Value);

        if (query.SubscriptionId.HasValue)
            queryable = queryable.Where(x => x.SubscriptionId == query.SubscriptionId.Value);

        if (!string.IsNullOrWhiteSpace(query.Currency))
        {
            var currency = query.Currency.Trim().ToUpperInvariant();
            queryable = queryable.Where(x => x.Currency == currency);
        }

        queryable = queryable.OrderByDescending(x => x.OccurredAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        return await queryable
            .Select(AdminBillingProjections.ToLedgerEntryDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
