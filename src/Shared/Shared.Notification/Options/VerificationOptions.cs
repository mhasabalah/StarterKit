namespace Shared.Notification.Options;

public sealed class VerificationOptions
{
    /// <summary>
    /// How long (in minutes) a generated OTP code remains valid. Default: 10.
    /// Configured under "Notification:Verification:CodeExpiryMinutes" in appsettings.
    /// </summary>
    public int CodeExpiryMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum number of failed validation attempts before the code is locked.
    /// Default: 5.
    /// Configured under "Notification:Verification:MaxAttempts" in appsettings.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;
}
