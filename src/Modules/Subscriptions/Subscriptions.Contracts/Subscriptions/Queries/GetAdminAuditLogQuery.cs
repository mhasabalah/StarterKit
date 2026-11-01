using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetAdminAuditLogQuery : BaseQuery, IQuery<PaginatedResult<AdminAuditDto>>
{
    public Guid? AdminId { get; init; }
    public string? Action { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
