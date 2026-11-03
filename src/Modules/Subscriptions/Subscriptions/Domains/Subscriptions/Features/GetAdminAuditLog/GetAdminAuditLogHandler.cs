using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetAdminAuditLog;

public class GetAdminAuditLogQueryValidator : AbstractValidator<GetAdminAuditLogQuery>
{
    public GetAdminAuditLogQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");
    }
}

public class GetAdminAuditLogHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetAdminAuditLogQuery, PaginatedResult<AdminAuditDto>>
{
    public async Task<PaginatedResult<AdminAuditDto>> Handle(
        GetAdminAuditLogQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.AdminAuditLogs.AsNoTracking();

        queryable = ApplyFilters(queryable, query);

        queryable = queryable.OrderByDescending(x => x.OccurredAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(x => new AdminAuditDto(
            x.Id,
            x.AdminId,
            x.Action,
            x.TargetId,
            x.TargetType,
            x.SubscriberId,
            x.Reason,
            x.OccurredAt,
            x.Amount,
            x.Currency,
            x.BeforeJson,
            x.AfterJson));

        return await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }

    private static IQueryable<AdminActionAuditLog> ApplyFilters(
        IQueryable<AdminActionAuditLog> queryable,
        GetAdminAuditLogQuery query)
    {
        if (query.AdminId.HasValue)
            queryable = queryable.Where(x => x.AdminId == query.AdminId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
            queryable = queryable.Where(x => x.Action == query.Action);

        // timestamptz parameters must be UTC (offset 0) - normalize client-supplied offsets.
        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            queryable = queryable.Where(x => x.OccurredAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            queryable = queryable.Where(x => x.OccurredAt <= toUtc);
        }

        return queryable;
    }
}
