using Shared.Contracts.Attributes;

namespace Identity.Services.Otp;

/// <summary>
/// What a one-time code is for. Each purpose has its own code, attempts, cooldown and send cap, and the purpose is
/// part of the HMAC input, so a code issued for one purpose never verifies another. Stored as int: never renumber.
/// </summary>
public enum OtpPurpose
{
    /// <summary>Signed-in user proves their phone (/api/identity/phone/*, and /api/notifications/verification/* PhoneVerification).</summary>
    [LocalizedDescription("Phone verification", "التحقق من الهاتف")]
    PhoneVerification = 1,

    /// <summary>SMS password reset (forgot-password + verify-reset-otp).</summary>
    [LocalizedDescription("Password reset", "إعادة تعيين كلمة المرور")]
    PasswordReset = 2,

    /// <summary>Onboarding phone code; verifying it signs the new user in (/api/identity/onboarding/*).</summary>
    [LocalizedDescription("Onboarding phone verification", "التحقق من الهاتف عند التسجيل")]
    OnboardingPhoneVerification = 3,

    /// <summary>Standalone email check (/api/notifications/verification/* EmailVerification).</summary>
    [LocalizedDescription("Email verification", "التحقق من البريد الإلكتروني")]
    EmailVerification = 4,

    /// <summary>Standalone two-factor check (/api/notifications/verification/* TwoFactorLogin).</summary>
    [LocalizedDescription("Two-factor login", "تسجيل الدخول بعاملين")]
    TwoFactorLogin = 5
}
