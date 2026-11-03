using System.Text.Json.Serialization;
using Shared.Contracts.Attributes;

namespace Identity.Domains.Onboarding.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OnboardingStatus
{
    [LocalizedDescription("Registered", "مسجل")]
    Registered = 0,

    [LocalizedDescription("OTP Sent", "تم إرسال رمز التحقق")]
    OtpSent = 1,

    [LocalizedDescription("Phone Verified", "تم التحقق من الهاتف")]
    PhoneVerified = 2,

    [LocalizedDescription("Complete", "مكتمل")]
    Complete = 3,

    [LocalizedDescription("Timed Out", "انتهت المهلة")]
    TimedOut = 4
}
