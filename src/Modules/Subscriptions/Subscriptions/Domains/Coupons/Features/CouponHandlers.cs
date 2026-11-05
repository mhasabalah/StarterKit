using Microsoft.Extensions.Localization;
using Subscriptions.Contracts.Subscriptions.Commands.Coupons;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Coupons.Exceptions;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Resources;

namespace Subscriptions.Domains.Coupons.Features;

/// <summary>Maps a coupon to its admin read model.</summary>
internal static class CouponProjections
{
    public static CouponDto ToDto(this Coupon x) => new(
        x.Id, x.Code, x.Description, x.DiscountType.ToString(), x.Value, x.Currency, x.Duration.ToString(),
        x.DurationPeriods, x.MaxRedemptions, x.RedemptionCount, x.ExpiresAt, x.IsActive,
        [.. x.AllowedPlanIds], x.Version, x.UpdatedAt);
}

public class CreateCouponCommandValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponCommandValidator(IStringLocalizer<SubscriptionsResource> localizer)
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Coupon.MaxCodeLength)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage(_ => localizer["Validation.Coupon.CodeFormat"]);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.Duration).IsInEnum();
        RuleFor(x => x.Value).GreaterThan(0m);
        RuleFor(x => x.Value).LessThanOrEqualTo(100m).When(x => x.DiscountType == CouponDiscountType.Percent);
        RuleFor(x => x.Currency).NotEmpty().Length(3).When(x => x.DiscountType == CouponDiscountType.FixedAmount);
        RuleFor(x => x.DurationPeriods).NotNull().GreaterThan(0).When(x => x.Duration == CouponDuration.Repeating);
        RuleFor(x => x.MaxRedemptions).GreaterThan(0).When(x => x.MaxRedemptions is not null);
    }
}

public class CreateCouponHandler(SubscriptionsDbContext dbContext, IAdminAuditWriter audit, TimeProvider timeProvider)
    : ICommandHandler<CreateCouponCommand, Guid>
{
    public async Task<Guid> Handle(CreateCouponCommand command, CancellationToken cancellationToken)
    {
        var code = Coupon.NormalizeCode(command.Code);
        if (await dbContext.Coupons.AnyAsync(x => x.Code == code, cancellationToken))
            throw new CouponAlreadyExistsException(code);

        var coupon = Coupon.Create(
            command.Code, command.Description, command.DiscountType, command.Value, command.Currency,
            command.Duration, command.DurationPeriods, command.MaxRedemptions, command.ExpiresAt,
            command.AllowedPlanIds, timeProvider.GetUtcNow());

        dbContext.Coupons.Add(coupon);
        audit.Record("CreateCoupon", coupon.Id, "Coupon",
            after: new { coupon.Code, DiscountType = coupon.DiscountType.ToString(), coupon.Value, Duration = coupon.Duration.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        return coupon.Id;
    }
}

public class UpdateCouponCommandValidator : AbstractValidator<UpdateCouponCommand>
{
    public UpdateCouponCommandValidator()
    {
        RuleFor(x => x.CouponId).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.MaxRedemptions).GreaterThan(0).When(x => x.MaxRedemptions is not null);
        RuleFor(x => x.Version!.Value).GreaterThanOrEqualTo(0).When(x => x.Version.HasValue);
    }
}

public class UpdateCouponHandler(SubscriptionsDbContext dbContext, IAdminAuditWriter audit, TimeProvider timeProvider)
    : ICommandHandler<UpdateCouponCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Coupons.FirstOrDefaultAsync(x => x.Id == command.CouponId, cancellationToken)
            ?? throw new CouponNotFoundException(command.CouponId);

        dbContext.ExpectVersion(coupon, command.Version);
        var before = new { coupon.IsActive, coupon.MaxRedemptions, coupon.ExpiresAt };

        coupon.Update(command.Description, command.MaxRedemptions, command.ExpiresAt, command.AllowedPlanIds,
            command.IsActive, timeProvider.GetUtcNow());

        audit.Record("UpdateCoupon", coupon.Id, "Coupon", before: before,
            after: new { coupon.IsActive, coupon.MaxRedemptions, coupon.ExpiresAt });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public class DeleteCouponHandler(SubscriptionsDbContext dbContext, IAdminAuditWriter audit)
    : ICommandHandler<DeleteCouponCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCouponCommand command, CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Coupons.FirstOrDefaultAsync(x => x.Id == command.CouponId, cancellationToken)
            ?? throw new CouponNotFoundException(command.CouponId);

        // Redeemed coupons are history (subscriptions and invoices refer to them): deactivate instead.
        if (coupon.RedemptionCount > 0 ||
            await dbContext.CouponRedemptions.AnyAsync(x => x.CouponId == coupon.Id, cancellationToken))
            throw new ConflictException("This coupon was already redeemed; deactivate it instead of deleting it.");

        dbContext.Coupons.Remove(coupon);
        audit.Record("DeleteCoupon", coupon.Id, "Coupon", before: new { coupon.Code });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public class GetCouponsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetCouponsQuery, PaginatedResult<CouponDto>>
{
    public async Task<PaginatedResult<CouponDto>> Handle(GetCouponsQuery query, CancellationToken cancellationToken)
    {
        var coupons = dbContext.Coupons.AsNoTracking().AsQueryable();

        if (query.IsActive is { } active)
            coupons = coupons.Where(x => x.IsActive == active);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            coupons = coupons.Where(x => x.Code.Contains(term));
        }

        var paged = await coupons
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Code)
            .Select(x => new
            {
                x.Id, x.Code, x.Description, x.DiscountType, x.Value, x.Currency, x.Duration, x.DurationPeriods,
                x.MaxRedemptions, x.RedemptionCount, x.ExpiresAt, x.IsActive, x.AllowedPlanIds, x.Version, x.UpdatedAt
            })
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        var items = (paged.Result ?? []).Select(x => new CouponDto(
            x.Id, x.Code, x.Description, x.DiscountType.ToString(), x.Value, x.Currency, x.Duration.ToString(),
            x.DurationPeriods, x.MaxRedemptions, x.RedemptionCount, x.ExpiresAt, x.IsActive,
            [.. x.AllowedPlanIds], x.Version, x.UpdatedAt)).ToList();

        return new PaginatedResult<CouponDto>
        {
            PageInfoResult = paged.PageInfoResult,
            Result = items
        };
    }
}

public class GetCouponByIdHandler(SubscriptionsDbContext dbContext) : IQueryHandler<GetCouponByIdQuery, CouponDto>
{
    public async Task<CouponDto> Handle(GetCouponByIdQuery query, CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Coupons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.CouponId, cancellationToken)
            ?? throw new CouponNotFoundException(query.CouponId);
        return coupon.ToDto();
    }
}
