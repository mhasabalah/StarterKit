namespace Identity.Contracts.Audit;

/// <summary>Paged, newest-first read of the Identity admin audit (permission <c>audit.read</c>).</summary>
public record GetIdentityAuditLogQuery : BaseQuery, IQuery<PaginatedResult<IdentityAuditLogDto>>
{
    /// <summary>Who performed the action.</summary>
    public Guid? AdminId { get; init; }

    /// <summary>Exact action name, e.g. <c>user.locked</c>, <c>2fa.reset</c>.</summary>
    public string? Action { get; init; }

    public string? TargetType { get; init; }

    public Guid? TargetId { get; init; }

    /// <summary>Inclusive lower bound of OccurredAt.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive upper bound of OccurredAt.</summary>
    public DateTimeOffset? To { get; init; }
}

public record IdentityAuditLogDto(
    Guid Id,
    Guid AdminId,
    string Action,
    string TargetType,
    Guid? TargetId,
    string? Reason,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    DateTimeOffset OccurredAt);
