using System.Security.Cryptography;
using System.Text;
using Identity.Contracts.Users.Queries;
using Microsoft.Extensions.Caching.Distributed;

namespace Shared.Notification.Services;

public sealed class VerificationService(
    ISender sender,
    IEnumerable<INotificationChannelAdapter> adapters,
    IDistributedCache cache,
    NotificationDbContext db,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<VerificationOptions> verificationOptions,
    ILogger<VerificationService> logger)
    : IVerificationService
{
    // Stored in Redis as JSON. Never exposed outside this class.
    private sealed record OtpPayload(
        string CodeHash,
        NotificationChannel Channel,
        int MaxAttempts,
        int Attempts,
        DateTime ExpiresAt);

    public async Task<SendVerificationResult> SendCodeAsync(
        Guid userId,
        VerificationPurpose purpose,
        NotificationCulture culture = NotificationCulture.En,
        CancellationToken ct = default)
    {
        var contact = await sender.Send(new GetUserContactInfoQuery(userId), ct);

        var channel = await ResolveChannelAsync(userId, purpose, ct);

        var adapterMap = adapters.ToDictionary(a => a.Channel);
        if (!adapterMap.TryGetValue(channel, out var adapter))
        {
            logger.LogError(
                "No adapter registered for channel {Channel}. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            return new SendVerificationResult(false, channel, $"Channel {channel} is not supported.");
        }

        if (!adapter.CanSend(contact.Email, contact.Phone))
        {
            logger.LogWarning(
                "Cannot send via {Channel} — required contact info missing. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            return new SendVerificationResult(
                false, channel,
                channel == NotificationChannel.WhatsApp
                    ? "User has no phone number on their profile."
                    : "User has no email address on their profile.");
        }

        // Bug 1 fix: RandomNumberGenerator.GetInt32 — uniform distribution, no overflow risk
        var plainCode = GenerateCode();
        var opts = verificationOptions.Value;
        var expiresAt = DateTime.UtcNow.AddMinutes(opts.CodeExpiryMinutes);

        var payload = new OtpPayload(
            CodeHash: HashCode(plainCode),
            Channel: channel,
            MaxAttempts: opts.MaxAttempts,
            Attempts: 0,
            ExpiresAt: expiresAt);

        var cacheKey = BuildKey(userId, purpose);
        var cacheOptions = new DistributedCacheEntryOptions
        {
            // Redis SET is atomic — overwrites any previous code for this user+purpose in one operation
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(opts.CodeExpiryMinutes)
        };
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(payload), cacheOptions, ct);

        if (channel != NotificationChannel.WhatsApp)
        {
            // TODO: OTP email templates (Liquid files) are not yet implemented.
            // EmailVerification and TwoFactorLogin via email will be supported in a future task.
            logger.LogWarning(
                "OTP delivery via {Channel} is not yet supported. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);
            try { await cache.RemoveAsync(cacheKey, ct); } catch { /* best effort */ }
            return new SendVerificationResult(false, channel, $"OTP delivery via {channel} is not yet available.");
        }

        var whatsAppTemplate = purpose switch
        {
            VerificationPurpose.PhoneVerification => WhatsAppTemplate.PhoneVerificationOtp,
            VerificationPurpose.TwoFactorLogin    => WhatsAppTemplate.TwoFactorOtp,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose),
                $"Verification purpose '{purpose}' has no WhatsApp template.")
        };

        var message = new NotificationMessage
        {
            UserId = userId,
            Culture = culture,
            WhatsApp = new WhatsAppPayload(
                whatsAppTemplate,
                new Dictionary<string, object> { ["code"] = plainCode })
        };

        try
        {
            await adapter.SendAsync(message, contact.Email, contact.Phone, ct);
        }
        catch (Exception ex)
        {
            // Bug 5 fix: wrap cleanup so delivery exception is never masked by a secondary failure
            try { await cache.RemoveAsync(cacheKey, ct); } catch { /* best effort */ }

            logger.LogError(ex,
                "Verification code delivery failed via {Channel}. UserId: {UserId} Purpose: {Purpose}",
                channel, userId, purpose);

            return new SendVerificationResult(false, channel, "Failed to deliver the verification code.");
        }

        logger.LogInformation(
            "Verification code sent via {Channel}. UserId: {UserId} Purpose: {Purpose}",
            channel, userId, purpose);

        return new SendVerificationResult(true, channel);
    }

    public async Task<VerificationResult> ValidateCodeAsync(
        Guid userId,
        string code,
        VerificationPurpose purpose,
        CancellationToken ct = default)
    {
        var cacheKey = BuildKey(userId, purpose);
        var raw = await cache.GetStringAsync(cacheKey, ct);

        // Expired TTL and NotFound are indistinguishable from Redis — both return null.
        // This is intentional: never reveal to the caller whether a code existed but expired.
        if (raw is null)
            return VerificationResult.NotFound;

        var payload = JsonSerializer.Deserialize<OtpPayload>(raw);
        if (payload is null)
            return VerificationResult.NotFound;

        // Edge case: Redis TTL hasn't fired yet but our own ExpiresAt has passed
        var remaining = payload.ExpiresAt - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            try { await cache.RemoveAsync(cacheKey, ct); } catch { /* best effort */ }
            return VerificationResult.NotFound;
        }

        if (payload.Attempts >= payload.MaxAttempts)
            return VerificationResult.MaxAttemptsReached;

        // Increment BEFORE hash comparison (prevents timing-based enumeration; preserves remaining TTL)
        var updated = payload with { Attempts = payload.Attempts + 1 };
        var updateOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = remaining
        };
        await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(updated), updateOptions, ct);

        if (HashCode(code) != payload.CodeHash)
        {
            logger.LogWarning(
                "Verification code mismatch. UserId: {UserId} Purpose: {Purpose} Attempts: {Attempts}/{Max}",
                userId, purpose, updated.Attempts, payload.MaxAttempts);
            return VerificationResult.Mismatch;
        }

        // Consumed — delete immediately so it cannot be replayed
        await cache.RemoveAsync(cacheKey, ct);

        logger.LogInformation(
            "Verification code validated successfully. UserId: {UserId} Purpose: {Purpose}",
            userId, purpose);

        return VerificationResult.Success;
    }

    // ----- Private helpers -----

    private async Task<NotificationChannel> ResolveChannelAsync(
        Guid userId,
        VerificationPurpose purpose,
        CancellationToken ct)
    {
        if (purpose == VerificationPurpose.PhoneVerification)
            return NotificationChannel.WhatsApp;

        if (purpose == VerificationPurpose.EmailVerification)
            return NotificationChannel.Email;

        // TwoFactorLogin: honour user preference; fall back to system default
        var preference = await db.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (preference is { ChannelOrder.Length: > 0 })
            return preference.ChannelOrder[0];

        // Bug 4 fix: guard against misconfigured empty DefaultChannelOrder
        return notificationOptions.Value.DefaultChannelOrder
            .FirstOrDefault(NotificationChannel.WhatsApp);
    }

    private static string BuildKey(Guid userId, VerificationPurpose purpose)
        => $"otp:{userId:N}:{(int)purpose}";

    // Bug 1 fix: GetInt32 is uniform and has no integer overflow risk
    private static string GenerateCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string code)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hashBytes);
    }
}
