using System.Text.Json.Serialization;
using Shared.Contracts.Attributes;

namespace Notification.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VerificationPurpose
{
    [LocalizedDescription("Phone Verification", "التحقق من الهاتف")]
    PhoneVerification = 1,

    [LocalizedDescription("Email Verification", "التحقق من البريد الإلكتروني")]
    EmailVerification = 2,

    [LocalizedDescription("Two-Factor Login", "تسجيل الدخول بعاملين")]
    TwoFactorLogin = 3,
}
