namespace Subscriptions.Domains.Coupons.Exceptions;

public class CouponNotFoundException : NotFoundException
{
    public CouponNotFoundException(Guid couponId) : base($"Coupon with ID '{couponId}' was not found.")
    {
    }

    public CouponNotFoundException(string code) : base($"Coupon '{code}' was not found.")
    {
    }
}

public class CouponAlreadyExistsException(string code)
    : ConflictException($"A coupon with code '{code}' already exists.");

public class CouponAlreadyRedeemedException()
    : ConflictException("This coupon was already redeemed by the subscriber.");

/// <summary>The coupon exists but cannot be used now. <see cref="CustomException.ErrorCode"/> says why (clients branch on it).</summary>
public class InvalidCouponException : BadRequestException
{
    public const string Inactive = "coupon_inactive";
    public const string Expired = "coupon_expired";
    public const string Exhausted = "coupon_exhausted";
    public const string PlanNotAllowed = "coupon_plan_not_allowed";
    public const string CurrencyMismatch = "coupon_currency_mismatch";

    public InvalidCouponException(string reasonCode, string message) : base(message)
    {
        ErrorCode = reasonCode;
    }
}
