using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetCouponsQuery : BaseQuery, IQuery<PaginatedResult<CouponDto>>
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
}

public record GetCouponByIdQuery(Guid CouponId) : IQuery<CouponDto>;
