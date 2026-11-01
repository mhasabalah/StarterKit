using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetActivePlanLimitsQuery(Guid SubscriberId) : IQuery<ActivePlanLimitsDto>;
