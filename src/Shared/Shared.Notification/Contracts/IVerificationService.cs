namespace Shared.Notification.Contracts;

/// <summary>
/// Synchronous OTP verification service. Does NOT use Hangfire.
/// Calls channel adapters directly so the caller gets an immediate success/failure result.
/// </summary>
public interface IVerificationService
{
    /// <summary>
    /// Generates a 6-digit OTP, saves it (hashed), and delivers it via the resolved channel.
    ///
    /// Channel resolution rules:
    ///   PhoneVerification  → always WhatsApp (requires phone number in user profile)
    ///   EmailVerification  → always Email    (requires email in user profile)
    ///   TwoFactorLogin     → reads UserNotificationPreference; falls back to NotificationOptions.DefaultChannelOrder[0]
    ///
    /// Any previous active codes for the same userId + purpose are invalidated before the new one is saved.
    /// </summary>
    Task<SendVerificationResult> SendCodeAsync(
        Guid userId,
        VerificationPurpose purpose,
        NotificationCulture culture = NotificationCulture.En,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a submitted code against the stored hash.
    /// Increments the attempt counter on every call (success or failure).
    /// Marks the code as used on success.
    /// Returns a result enum — never throws for business validation failures.
    /// </summary>
    Task<VerificationResult> ValidateCodeAsync(
        Guid userId,
        string code,
        VerificationPurpose purpose,
        CancellationToken ct = default);
}

/// <summary>Returned by SendCodeAsync.</summary>
public sealed record SendVerificationResult(
    bool Success,
    NotificationChannel Channel,
    string? Error = null);

/// <summary>Returned by ValidateCodeAsync.</summary>
public enum VerificationResult
{
    /// <summary>Code matched. Mark as used.</summary>
    Success = 0,

    /// <summary>No active code found for this user + purpose.</summary>
    NotFound = 1,

    /// <summary>Code exists but ExpiresAt is in the past.</summary>
    Expired = 2,

    /// <summary>Code was already successfully validated (UsedAt is set).</summary>
    AlreadyUsed = 3,

    /// <summary>Attempts >= MaxAttempts. Do not reveal to the client — return generic error.</summary>
    MaxAttemptsReached = 4,

    /// <summary>Hash did not match.</summary>
    Mismatch = 5,
}
