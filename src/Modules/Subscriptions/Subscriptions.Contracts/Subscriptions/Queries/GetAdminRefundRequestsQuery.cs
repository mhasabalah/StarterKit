using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetAdminRefundRequestsQuery : BaseQuery, IQuery<PaginatedResult<AdminRefundRequestDto>>
{
    public RefundStatus? Status { get; init; }
}
