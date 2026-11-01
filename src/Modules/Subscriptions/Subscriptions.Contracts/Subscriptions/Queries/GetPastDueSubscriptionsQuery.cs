using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPastDueSubscriptionsQuery : BaseQuery, IQuery<PaginatedResult<PastDueSubscriptionDto>>;
