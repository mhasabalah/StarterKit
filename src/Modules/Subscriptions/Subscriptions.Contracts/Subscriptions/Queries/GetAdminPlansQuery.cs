using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetAdminPlansQuery : BaseQuery, IQuery<PaginatedResult<AdminPlanDto>>;
