using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPlanFeaturesQuery(Guid PlanId) : IQuery<List<AdminPlanFeatureDto>>;
