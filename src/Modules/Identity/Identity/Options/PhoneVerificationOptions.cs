namespace Identity.Options;

/// <summary>
/// Phone verification settings
/// </summary>
public class PhoneVerificationOptions
{
    public const string SectionName = "PhoneVerification";
    
    public int CodeLength { get; init; } = 6;
    public int CodeExpirationMinutes { get; init; } = 10;
    public int MaxVerificationAttempts { get; init; } = 5;
    public int ResendCooldownSeconds { get; init; } = 60;
}
