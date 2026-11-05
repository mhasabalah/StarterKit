using Shared.Core.Exceptions;
using FluentAssertions;
using Xunit;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Coupons.Exceptions;
using Subscriptions.Domains.Coupons.Models;

namespace Subscriptions.Tests.Domain;

public class CouponTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PlanA = Guid.NewGuid();

    private static Coupon Percent(
        int? max = null, DateTimeOffset? expires = null, IEnumerable<Guid>? plans = null,
        CouponDuration duration = CouponDuration.Once, int? periods = null) =>
        Coupon.Create(" save10 ", null, CouponDiscountType.Percent, 10m, null, duration, periods, max, expires, plans, Now);

    [Fact]
    public void Codes_are_trimmed_and_upper_cased() => Percent().Code.Should().Be("SAVE10");

    [Theory]
    [InlineData(CouponDiscountType.Percent, 0, null)]
    [InlineData(CouponDiscountType.Percent, 101, null)]
    [InlineData(CouponDiscountType.FixedAmount, 10, null)]  // currency missing
    [InlineData(CouponDiscountType.FixedAmount, 0, "SAR")]
    public void Invalid_terms_are_rejected(CouponDiscountType type, double value, string? currency) =>
        FluentActions.Invoking(() => Coupon.Create("X", null, type, (decimal)value, currency, CouponDuration.Once, null, null, null, null, Now))
            .Should().Throw<BadRequestException>();

    [Fact]
    public void A_repeating_coupon_needs_a_period_count() =>
        FluentActions.Invoking(() => Percent(duration: CouponDuration.Repeating, periods: null))
            .Should().Throw<BadRequestException>();

    [Fact]
    public void Redeeming_counts_and_exposes_the_terms_to_snapshot()
    {
        var coupon = Percent(duration: CouponDuration.Repeating, periods: 3);

        var redemption = coupon.Redeem(Guid.NewGuid(), Guid.NewGuid(), PlanA, "SAR", Now);

        coupon.RedemptionCount.Should().Be(1);
        redemption.CouponId.Should().Be(coupon.Id);
        coupon.Terms.PeriodsRemaining.Should().Be(3);
    }

    [Fact]
    public void An_inactive_coupon_is_refused() => AssertRefused(Deactivated(), InvalidCouponException.Inactive);

    [Fact]
    public void An_expired_coupon_is_refused() =>
        AssertRefused(Percent(expires: Now.AddMinutes(-1)), InvalidCouponException.Expired);

    [Fact]
    public void A_fully_redeemed_coupon_is_refused()
    {
        var coupon = Percent(max: 1);
        coupon.Redeem(Guid.NewGuid(), Guid.NewGuid(), PlanA, "SAR", Now);

        AssertRefused(coupon, InvalidCouponException.Exhausted);
    }

    [Fact]
    public void A_coupon_restricted_to_other_plans_is_refused() =>
        AssertRefused(Percent(plans: [Guid.NewGuid()]), InvalidCouponException.PlanNotAllowed);

    [Fact]
    public void A_fixed_coupon_is_refused_on_another_currency()
    {
        var coupon = Coupon.Create("FLAT", null, CouponDiscountType.FixedAmount, 5m, "usd", CouponDuration.Once, null, null, null, null, Now);

        AssertRefused(coupon, InvalidCouponException.CurrencyMismatch);
        coupon.EnsureRedeemable(PlanA, "USD", Now);
    }

    [Fact]
    public void Max_redemptions_cannot_drop_below_what_was_redeemed()
    {
        var coupon = Percent(max: 5);
        coupon.Redeem(Guid.NewGuid(), Guid.NewGuid(), PlanA, "SAR", Now);
        coupon.Redeem(Guid.NewGuid(), Guid.NewGuid(), PlanA, "SAR", Now);

        FluentActions.Invoking(() => coupon.Update(null, 1, null, null, true, Now)).Should().Throw<ConflictException>();
    }

    private static Coupon Deactivated()
    {
        var coupon = Percent();
        coupon.Deactivate(Now);
        return coupon;
    }

    private static void AssertRefused(Coupon coupon, string reason) =>
        FluentActions.Invoking(() => coupon.EnsureRedeemable(PlanA, "SAR", Now))
            .Should().Throw<InvalidCouponException>().Which.ErrorCode.Should().Be(reason);
}
