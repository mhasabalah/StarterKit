namespace Identity.Options;

/// <summary>
/// One-time code policy of the unified IOtpCodeService for every purpose (code length, cooldown and send cap for all;
/// lifetime and attempts for all but PasswordReset, which uses PasswordResetOptions.OtpExpiryMinutes / MaxOtpAttempts).
/// </summary>
public class PhoneVerificationOptions
{
    public const string SectionName = "PhoneVerification";

    public int CodeLength { get; init; } = 6;
    public int CodeExpirationMinutes { get; init; } = 10;
    public int MaxVerificationAttempts { get; init; } = 5;
    public int ResendCooldownSeconds { get; init; } = 60;

    /// <summary>Maximum codes sent per user and purpose in a rolling 24h window (SMS cost + brute-force cap).</summary>
    public int MaxSendsPerDay { get; init; } = 10;
}
