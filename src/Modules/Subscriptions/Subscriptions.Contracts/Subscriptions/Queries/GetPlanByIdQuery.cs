using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPlanByIdQuery(Guid PlanId) : IQuery<AdminPlanDto?>;
