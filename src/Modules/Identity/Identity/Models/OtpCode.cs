using Identity.Services.Otp;

namespace Identity.Models;

/// <summary>
/// The current one-time code of a user for one <see cref="OtpPurpose"/> (at most one row per user and purpose).
/// The plaintext code is never stored: <see cref="CodeHash"/> is HMAC-SHA256(server key, purpose:user:code).
/// </summary>
public class OtpCode
{
    public Guid UserId { get; set; }

    public OtpPurpose Purpose { get; set; }

    /// <summary>Base64 HMAC-SHA256 of the code. Never the code itself.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Verification attempts used for the current code. Incremented atomically BEFORE comparing.</summary>
    public int Attempts { get; set; }

    /// <summary>Attempts allowed for the current code; when reached the code is locked until a new one is sent.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>When the current code was sent (resend cooldown).</summary>
    public DateTime LastSentAt { get; set; }

    /// <summary>Start of the current 24h send window (daily send cap).</summary>
    public DateTime WindowStartedAt { get; set; }

    /// <summary>Codes sent in the current 24h window.</summary>
    public int SendCount { get; set; }
}
