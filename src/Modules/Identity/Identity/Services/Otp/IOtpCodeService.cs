using System.Net;

namespace Identity.Services.Otp;

/// <summary>
/// THE one-time-code implementation of the application (phone verification, SMS password reset, onboarding and the
/// standalone /notifications/verification endpoints). Codes come from a CSPRNG and are stored only as
/// HMAC-SHA256(<c>Identity:OtpHmacKey</c>, purpose:user:code) in <c>identity.otp_codes</c> (one row per user and
/// purpose). Issuing atomically enforces the resend cooldown and the per-window send cap and replaces (invalidates)
/// the previous code; verifying atomically reserves an attempt BEFORE the constant-time compare and consumes the
/// code on success (single use). Delivery is not done here: SMS via <c>ISmsService</c>, WhatsApp/email via
/// Notification's <c>IOtpDeliveryService</c> (see <see cref="IOtpSender"/>).
/// </summary>
public interface IOtpCodeService
{
    /// <summary>
    /// Creates a new code (invalidating any previous one) and returns the plaintext so the caller can deliver it.
    /// Throws <see cref="OtpRateLimitedException"/> during the resend cooldown or when the send cap is reached.
    /// </summary>
    Task<OtpIssueResult> IssueAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default);

    /// <summary>Verifies and, on success, consumes the code (single use).</summary>
    Task<OtpVerificationResult> VerifyAsync(Guid userId, OtpPurpose purpose, string code, CancellationToken ct = default);

    /// <summary>
    /// The code could not be delivered: invalidate exactly that code and lift the resend cooldown so the user can ask
    /// again right away. The send still counts against the cap. No-op if a newer code was issued meanwhile.
    /// </summary>
    Task ReleaseAsync(Guid userId, OtpPurpose purpose, OtpIssueResult issued, CancellationToken ct = default);

    /// <summary>Invalidates the current code of that purpose, if any (cooldown and cap are kept).</summary>
    Task InvalidateAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default);
}

public sealed record OtpIssueResult(string Code, DateTime ExpiresAt);

public enum OtpVerificationStatus
{
    Success,

    /// <summary>No code, or it was already used, replaced or invalidated.</summary>
    NotFound,
    Expired,
    Mismatch,

    /// <summary>Attempt limit reached; only a new code helps.</summary>
    Locked
}

public sealed record OtpVerificationResult(OtpVerificationStatus Status, int RemainingAttempts)
{
    public bool Succeeded => Status == OtpVerificationStatus.Success;
}

public enum OtpRateLimitReason
{
    /// <summary>A code was issued less than the resend cooldown ago.</summary>
    Cooldown,

    /// <summary>The per user + purpose send cap of the rolling 24h window is reached.</summary>
    SendCapReached
}

/// <summary>Resend cooldown or send cap hit. Maps to HTTP 429.</summary>
public sealed class OtpRateLimitedException(
    string message, TimeSpan retryAfter, OtpRateLimitReason reason = OtpRateLimitReason.Cooldown)
    : CustomException(message, HttpStatusCode.TooManyRequests)
{
    public TimeSpan RetryAfter { get; } = retryAfter;

    public OtpRateLimitReason Reason { get; } = reason;
}
