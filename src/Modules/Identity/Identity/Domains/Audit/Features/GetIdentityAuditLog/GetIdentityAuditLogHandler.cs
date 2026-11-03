using Identity.Contracts.Audit;
using Identity.Domains.Audit.Models;
using Shared.Core.Pagination;

namespace Identity.Domains.Audit.Features.GetIdentityAuditLog;

public class GetIdentityAuditLogQueryValidator : BaseQueryValidator<GetIdentityAuditLogQuery>
{
    public GetIdentityAuditLogQueryValidator()
    {
        RuleFor(x => x.Action).MaximumLength(100);
        RuleFor(x => x.TargetType).MaximumLength(50);
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithName("From")
            .WithMessage("From must be before or equal to To.");
    }
}

/// <summary>Newest first (OccurredAt, then Id for a stable order). Filters combine with AND.</summary>
public class GetIdentityAuditLogHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetIdentityAuditLogQuery, PaginatedResult<IdentityAuditLogDto>>
{
    public Task<PaginatedResult<IdentityAuditLogDto>> Handle(
        GetIdentityAuditLogQuery query,
        CancellationToken cancellationToken)
    {
        var logs = Filter(dbContext.AdminAuditLogs.AsNoTracking(), query)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new IdentityAuditLogDto(
                x.Id,
                x.AdminId,
                x.Action,
                x.TargetType,
                x.TargetId,
                x.Reason,
                x.BeforeJson,
                x.AfterJson,
                x.IpAddress,
                x.UserAgent,
                x.CorrelationId,
                x.OccurredAt));

        return logs.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }

    private static IQueryable<AdminActionAuditLog> Filter(
        IQueryable<AdminActionAuditLog> logs,
        GetIdentityAuditLogQuery query)
    {
        if (query.AdminId.HasValue)
            logs = logs.Where(x => x.AdminId == query.AdminId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
            logs = logs.Where(x => x.Action == query.Action);

        if (!string.IsNullOrWhiteSpace(query.TargetType))
            logs = logs.Where(x => x.TargetType == query.TargetType);

        if (query.TargetId.HasValue)
            logs = logs.Where(x => x.TargetId == query.TargetId.Value);

        // timestamptz parameters must be UTC (offset 0) - normalize client-supplied offsets.
        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            logs = logs.Where(x => x.OccurredAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            logs = logs.Where(x => x.OccurredAt <= toUtc);
        }

        return logs;
    }
}
