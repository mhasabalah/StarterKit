using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPendingSubscriptionsQuery : BaseQuery, IQuery<PaginatedResult<PendingSubscriptionDto>>;
