using Shared.Core.MultiTenancy.Web;
using Subscriptions.Contracts.Subscriptions.Commands.Coupons;
using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Coupons.Features;

public record CreateCouponRequest(
    string Code,
    string? Description,
    CouponDiscountType DiscountType,
    decimal Value,
    string? Currency,
    CouponDuration Duration,
    int? DurationPeriods,
    int? MaxRedemptions,
    DateTimeOffset? ExpiresAt,
    List<Guid>? AllowedPlanIds);

public record UpdateCouponRequest(
    string? Description,
    int? MaxRedemptions,
    DateTimeOffset? ExpiresAt,
    List<Guid>? AllowedPlanIds,
    bool IsActive,
    long? Version = null);

/// <summary>Admin CRUD of coupons (<c>billing.coupons.manage</c>).</summary>
public class CouponsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/admin/coupons")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);

        group.MapGet("/", async ([AsParameters] GetCouponsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .RequirePermission(Permissions.BillingCouponsManage)
            .WithName("GetCoupons");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetCouponByIdQuery(id), ct))))
            .RequirePermission(Permissions.BillingCouponsManage)
            .WithName("GetCouponById")
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateCouponRequest r, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new CreateCouponCommand(
                    r.Code, r.Description, r.DiscountType, r.Value, r.Currency, r.Duration, r.DurationPeriods,
                    r.MaxRedemptions, r.ExpiresAt, r.AllowedPlanIds), ct);
                return Results.Created($"{EndpointConfig.BaseApiPath}/admin/coupons/{id}",
                    ApiResponseExtensions.Success(new { couponId = id }));
            })
            .RequirePermission(Permissions.BillingCouponsManage)
            .RequireRateLimiting("create")
            .WithName("CreateCoupon")
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, UpdateCouponRequest r, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateCouponCommand(
                    id, r.Description, r.MaxRedemptions, r.ExpiresAt, r.AllowedPlanIds, r.IsActive, r.Version), ct);
                return Results.Ok(ApiResponseExtensions.Success(new { couponId = id }));
            })
            .RequirePermission(Permissions.BillingCouponsManage)
            .WithName("UpdateCoupon")
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteCouponCommand(id), ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.BillingCouponsManage)
            .DisallowImpersonation()
            .WithName("DeleteCoupon")
            .Produces(StatusCodes.Status409Conflict);
    }
}
