using Identity.Services.Otp;

namespace Identity.Domains.Verification;

/// <summary>
/// Purposes of the standalone /api/notifications/verification endpoints (public enum in Notification.Contracts,
/// which also picks the delivery channel) mapped onto the unified Identity OTP purposes.
/// </summary>
public static class VerificationPurposeExtensions
{
    public static OtpPurpose ToOtpPurpose(this VerificationPurpose purpose) => purpose switch
    {
        VerificationPurpose.PhoneVerification => OtpPurpose.PhoneVerification,
        VerificationPurpose.EmailVerification => OtpPurpose.EmailVerification,
        VerificationPurpose.TwoFactorLogin => OtpPurpose.TwoFactorLogin,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };
}
