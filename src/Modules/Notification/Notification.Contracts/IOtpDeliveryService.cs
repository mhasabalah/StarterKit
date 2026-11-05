namespace Notification.Contracts;

/// <summary>
/// Delivers a one-time code that ANOTHER module issued (Identity owns issuing, hashing, attempts, cooldowns and
/// rate limits - <c>Identity.Services.Otp.IOtpCodeService</c>). Notification only picks the channel and sends.
/// Synchronous (no Hangfire job) so the caller learns immediately whether the code reached the user and can
/// release the code when it did not. Never stores or logs the code.
/// </summary>
public interface IOtpDeliveryService
{
    /// <summary>
    /// Sends <paramref name="code"/> to the user. Channel resolution:
    ///   PhoneVerification → WhatsApp (needs a phone number on the profile)
    ///   EmailVerification → Email    (needs an email address; OneTimeCode email template)
    ///   TwoFactorLogin    → the user's first preferred channel, else NotificationOptions.DefaultChannelOrder[0]
    /// Never throws for delivery failures; returns <see cref="OtpDeliveryResult.Failure"/> instead.
    /// </summary>
    Task<OtpDeliveryResult> SendAsync(
        Guid userId,
        VerificationPurpose purpose,
        string code,
        NotificationCulture culture = NotificationCulture.En,
        CancellationToken ct = default);
}

/// <summary>Returned by <see cref="IOtpDeliveryService.SendAsync"/>.</summary>
public sealed record OtpDeliveryResult(
    NotificationChannel Channel,
    OtpDeliveryFailure Failure = OtpDeliveryFailure.None,
    string? Error = null)
{
    public bool Success => Failure == OtpDeliveryFailure.None;

    public static OtpDeliveryResult Failed(NotificationChannel channel, OtpDeliveryFailure failure, string error)
        => new(channel, failure, error);
}

/// <summary>Why <see cref="IOtpDeliveryService.SendAsync"/> did not deliver the code.</summary>
public enum OtpDeliveryFailure
{
    None = 0,
    ChannelUnavailable = 1,
    MissingContact = 2,
    DeliveryFailed = 3,
}
