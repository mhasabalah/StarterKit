using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPendingPaymentsQuery : BaseQuery, IQuery<PaginatedResult<PendingPaymentDto>>;
