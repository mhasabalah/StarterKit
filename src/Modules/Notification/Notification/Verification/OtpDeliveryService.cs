using UserDirectory.Contracts.Queries;

namespace Notification.Verification;

/// <summary>
/// Delivery-only side of one-time codes: resolves the channel for the purpose, checks the contact exists and calls
/// the channel adapter directly (not through the dispatch job), so the caller gets an immediate result.
/// Issuing, hashing, attempts, cooldowns and rate limits are owned by Identity (<c>IOtpCodeService</c>).
/// </summary>
public sealed class OtpDeliveryService(
    ISender sender,
    IEnumerable<INotificationChannelAdapter> adapters,
    INotificationPreferenceRepository preferences,
    IOptions<NotificationOptions> notificationOptions,
    ILogger<OtpDeliveryService> logger)
    : IOtpDeliveryService
{
    public async Task<OtpDeliveryResult> SendAsync(
        Guid userId,
        VerificationPurpose purpose,
        string code,
        NotificationCulture culture = NotificationCulture.En,
        CancellationToken ct = default)
    {
        var channel = await ResolveChannelAsync(userId, purpose, ct);

        var adapter = adapters.FirstOrDefault(a => a.Channel == channel);
        if (adapter is null)
        {
            logger.LogError(
                "No adapter registered for channel {Channel}. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            return OtpDeliveryResult.Failed(channel, OtpDeliveryFailure.ChannelUnavailable,
                $"Channel {channel} is not supported.");
        }

        var contact = await sender.Send(new GetUserContactInfoQuery(userId), ct);
        if (!adapter.CanSend(contact.Email, contact.Phone))
        {
            logger.LogWarning(
                "Cannot send via {Channel} - required contact info missing. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            return OtpDeliveryResult.Failed(channel, OtpDeliveryFailure.MissingContact,
                channel == NotificationChannel.WhatsApp
                    ? "User has no phone number on their profile."
                    : "User has no email address on their profile.");
        }

        var whatsAppTemplate = WhatsAppTemplateFor(purpose);
        if (channel != NotificationChannel.WhatsApp || whatsAppTemplate is null)
        {
            // TODO: OTP email templates are not yet implemented (EmailVerification / TwoFactorLogin via email).
            logger.LogWarning(
                "OTP delivery via {Channel} is not yet supported. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            return OtpDeliveryResult.Failed(channel, OtpDeliveryFailure.ChannelUnavailable,
                $"OTP delivery via {channel} is not yet available.");
        }

        var message = new NotificationMessage
        {
            UserId = userId,
            Culture = culture,
            WhatsApp = new WhatsAppPayload(whatsAppTemplate.Value, new Dictionary<string, object> { ["code"] = code })
        };

        try
        {
            await adapter.SendAsync(message, contact.Email, contact.Phone, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "One-time code delivery failed via {Channel}. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            return OtpDeliveryResult.Failed(channel, OtpDeliveryFailure.DeliveryFailed,
                "Failed to deliver the verification code.");
        }

        logger.LogInformation(
            "One-time code delivered via {Channel}. UserId: {UserId} Purpose: {Purpose}", channel, userId, purpose);

        return new OtpDeliveryResult(channel);
    }

    private async Task<NotificationChannel> ResolveChannelAsync(
        Guid userId, VerificationPurpose purpose, CancellationToken ct)
    {
        switch (purpose)
        {
            case VerificationPurpose.PhoneVerification:
                return NotificationChannel.WhatsApp;
            case VerificationPurpose.EmailVerification:
                return NotificationChannel.Email;
        }

        // TwoFactorLogin: honour the user's preference; fall back to the system default.
        var order = await preferences.GetChannelOrderAsync(userId, ct);
        if (order is { Length: > 0 })
            return order[0];

        return notificationOptions.Value.DefaultChannelOrder.FirstOrDefault(NotificationChannel.WhatsApp);
    }

    private static WhatsAppTemplate? WhatsAppTemplateFor(VerificationPurpose purpose) => purpose switch
    {
        VerificationPurpose.PhoneVerification => WhatsAppTemplate.PhoneVerificationOtp,
        VerificationPurpose.TwoFactorLogin => WhatsAppTemplate.TwoFactorOtp,
        _ => null
    };
}
