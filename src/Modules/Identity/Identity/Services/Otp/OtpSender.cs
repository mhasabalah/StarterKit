namespace Identity.Services.Otp;

/// <summary>
/// Issues a code with <see cref="IOtpCodeService"/> and hands it to Notification (<see cref="IOtpDeliveryService"/>,
/// WhatsApp / email) for delivery. When delivery fails the code is released (invalidated, cooldown lifted) so the
/// user is not locked out by a code they never received. Never throws for rate limits or delivery failures.
/// The SMS paths (phone verification, password reset) deliver through <c>ISmsService</c> instead.
/// </summary>
public interface IOtpSender
{
    Task<OtpSendResult> SendAsync(
        Guid userId,
        OtpPurpose purpose,
        NotificationCulture culture = NotificationCulture.En,
        CancellationToken ct = default);
}

public enum OtpSendFailure
{
    None = 0,
    Cooldown = 1,
    SendCapReached = 2,
    ChannelUnavailable = 3,
    MissingContact = 4,
    DeliveryFailed = 5,
}

public sealed record OtpSendResult(
    OtpSendFailure Failure,
    NotificationChannel? Channel = null,
    DateTime? ExpiresAt = null,
    string? Error = null,
    TimeSpan? RetryAfter = null)
{
    public bool Success => Failure == OtpSendFailure.None;

    public bool IsRateLimited => Failure is OtpSendFailure.Cooldown or OtpSendFailure.SendCapReached;
}

public sealed class OtpSender(
    IOtpCodeService otpCodeService,
    IOtpDeliveryService deliveryService,
    ILogger<OtpSender> logger) : IOtpSender
{
    public async Task<OtpSendResult> SendAsync(
        Guid userId,
        OtpPurpose purpose,
        NotificationCulture culture = NotificationCulture.En,
        CancellationToken ct = default)
    {
        var deliveryPurpose = DeliveryPurposeFor(purpose);

        OtpIssueResult issued;
        try
        {
            issued = await otpCodeService.IssueAsync(userId, purpose, ct);
        }
        catch (OtpRateLimitedException ex)
        {
            logger.LogWarning(
                "{Purpose} code for user {UserId} refused ({Reason}); retry after {RetryAfter}",
                purpose, userId, ex.Reason, ex.RetryAfter);

            return new OtpSendResult(
                ex.Reason == OtpRateLimitReason.Cooldown ? OtpSendFailure.Cooldown : OtpSendFailure.SendCapReached,
                Error: ex.Message,
                RetryAfter: ex.RetryAfter);
        }

        var delivery = await deliveryService.SendAsync(userId, deliveryPurpose, issued.Code, culture, ct);
        if (delivery.Success)
        {
            return new OtpSendResult(OtpSendFailure.None, delivery.Channel, issued.ExpiresAt);
        }

        // The user got nothing: the code must not count against them (cooldown lifted; the send cap still counts).
        await otpCodeService.ReleaseAsync(userId, purpose, issued, CancellationToken.None);

        return new OtpSendResult(
            delivery.Failure switch
            {
                OtpDeliveryFailure.MissingContact => OtpSendFailure.MissingContact,
                OtpDeliveryFailure.ChannelUnavailable => OtpSendFailure.ChannelUnavailable,
                _ => OtpSendFailure.DeliveryFailed
            },
            delivery.Channel,
            Error: delivery.Error);
    }

    private static VerificationPurpose DeliveryPurposeFor(OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.PhoneVerification or OtpPurpose.OnboardingPhoneVerification => VerificationPurpose.PhoneVerification,
        OtpPurpose.EmailVerification => VerificationPurpose.EmailVerification,
        OtpPurpose.TwoFactorLogin => VerificationPurpose.TwoFactorLogin,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose,
            "This purpose is delivered by SMS (ISmsService), not through Notification.")
    };
}
