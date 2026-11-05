using System.Globalization;
using System.Security.Cryptography;
using Identity.Data.Repository.Otp;
using Identity.Models;
using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Services.Otp;

public sealed class OtpCodeService(
    IOtpCodeRepository repository,
    IOptions<IdentitySecurityOptions> securityOptions,
    IOptions<PhoneVerificationOptions> phoneOptions,
    IOptions<PasswordResetOptions> resetOptions,
    TimeProvider timeProvider,
    ILogger<OtpCodeService> logger) : IOtpCodeService
{
    /// <summary>Rolling window of the send cap.</summary>
    public static readonly TimeSpan SendWindow = TimeSpan.FromDays(1);

    /// <summary>Longer input is cut before hashing (bounded work; never a valid code anyway).</summary>
    private const int MaxSubmittedCodeLength = 32;

    private readonly byte[] _key = Encoding.UTF8.GetBytes(securityOptions.Value.OtpHmacKey);

    public async Task<OtpIssueResult> IssueAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default)
    {
        var policy = PolicyFor(purpose);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var code = GenerateCode(policy.CodeLength);
        var expiresAt = now + policy.Lifetime;

        var issued = await repository.TryIssueAsync(
            new OtpCode
            {
                UserId = userId,
                Purpose = purpose,
                CodeHash = ComputeHash(userId, purpose, code),
                ExpiresAt = expiresAt,
                Attempts = 0,
                MaxAttempts = policy.MaxAttempts,
                LastSentAt = now,
                WindowStartedAt = now,
                SendCount = 1
            },
            cooldownBoundary: now - policy.ResendCooldown,
            windowBoundary: now - SendWindow,
            maxSendsPerWindow: policy.MaxSendsPerDay,
            ct);

        if (!issued)
        {
            throw await RateLimitedAsync(userId, purpose, policy, now, ct);
        }

        return new OtpIssueResult(code, expiresAt);
    }

    public async Task<OtpVerificationResult> VerifyAsync(
        Guid userId, OtpPurpose purpose, string code, CancellationToken ct = default)
    {
        var stored = await repository.GetAsync(userId, purpose, ct);
        if (stored is null || string.IsNullOrEmpty(stored.CodeHash))
        {
            return new OtpVerificationResult(OtpVerificationStatus.NotFound, 0);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now >= stored.ExpiresAt)
        {
            return new OtpVerificationResult(OtpVerificationStatus.Expired, 0);
        }

        if (stored.Attempts >= stored.MaxAttempts)
        {
            return new OtpVerificationResult(OtpVerificationStatus.Locked, 0);
        }

        // Reserve the attempt atomically BEFORE comparing, so N parallel guesses cannot exceed MaxAttempts.
        if (!await repository.TryConsumeAttemptAsync(userId, purpose, stored.CodeHash, ct))
        {
            // Either the limit was reached meanwhile, or the code was consumed/replaced by a parallel request.
            var current = await repository.GetAsync(userId, purpose, ct);
            return current?.CodeHash == stored.CodeHash
                ? new OtpVerificationResult(OtpVerificationStatus.Locked, 0)
                : new OtpVerificationResult(OtpVerificationStatus.NotFound, 0);
        }

        var remaining = Math.Max(0, stored.MaxAttempts - (stored.Attempts + 1));

        if (!FixedTimeEquals(ComputeHash(userId, purpose, Normalize(code)), stored.CodeHash))
        {
            logger.LogWarning(
                "Invalid {Purpose} code for user {UserId}; {Remaining} attempts left", purpose, userId, remaining);

            return new OtpVerificationResult(
                remaining == 0 ? OtpVerificationStatus.Locked : OtpVerificationStatus.Mismatch, remaining);
        }

        // Single use: only the request that actually clears the code wins.
        return await repository.TryConsumeAsync(userId, purpose, stored.CodeHash, ct)
            ? new OtpVerificationResult(OtpVerificationStatus.Success, remaining)
            : new OtpVerificationResult(OtpVerificationStatus.NotFound, 0);
    }

    public Task ReleaseAsync(Guid userId, OtpPurpose purpose, OtpIssueResult issued, CancellationToken ct = default)
    {
        var rewoundLastSentAt = timeProvider.GetUtcNow().UtcDateTime - PolicyFor(purpose).ResendCooldown;
        return repository.InvalidateAsync(
            userId, purpose, ComputeHash(userId, purpose, issued.Code), rewoundLastSentAt, ct);
    }

    public Task InvalidateAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default)
        => repository.InvalidateAsync(userId, purpose, onlyIfCodeHash: null, lastSentAt: null, ct);

    /// <summary>HMAC-SHA256(key, "purpose:userId:code"), so a hash is bound to one user and one purpose.</summary>
    public string ComputeHash(Guid userId, OtpPurpose purpose, string code) =>
        Convert.ToBase64String(
            HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{(int)purpose}:{userId:N}:{code}")));

    /// <summary>Uniformly random numeric code from a CSPRNG, zero-padded to <paramref name="length"/> digits.</summary>
    public static string GenerateCode(int length)
    {
        var upperBound = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(0, upperBound).ToString($"D{length}", CultureInfo.InvariantCulture);
    }

    private async Task<OtpRateLimitedException> RateLimitedAsync(
        Guid userId, OtpPurpose purpose, OtpPolicy policy, DateTime now, CancellationToken ct)
    {
        var existing = await repository.GetAsync(userId, purpose, ct);
        var cooldownEndsAt = (existing?.LastSentAt ?? now) + policy.ResendCooldown;

        if (existing is not null && now < cooldownEndsAt)
        {
            return new OtpRateLimitedException(
                "Please wait before requesting a new code.", cooldownEndsAt - now, OtpRateLimitReason.Cooldown);
        }

        logger.LogWarning("Daily {Purpose} code limit reached for user {UserId}", purpose, userId);
        var windowEndsAt = (existing?.WindowStartedAt ?? now) + SendWindow;
        return new OtpRateLimitedException(
            "Too many codes requested. Please try again later.",
            windowEndsAt > now ? windowEndsAt - now : TimeSpan.Zero,
            OtpRateLimitReason.SendCapReached);
    }

    private static string Normalize(string? code)
    {
        var trimmed = (code ?? string.Empty).Trim();
        return trimmed.Length > MaxSubmittedCodeLength ? trimmed[..MaxSubmittedCodeLength] : trimmed;
    }

    private static bool FixedTimeEquals(string leftBase64, string rightBase64) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(leftBase64),
            Convert.FromBase64String(rightBase64));

    private OtpPolicy PolicyFor(OtpPurpose purpose)
    {
        var phone = phoneOptions.Value;
        var codeLength = Math.Clamp(phone.CodeLength, 4, 9);
        var cooldown = TimeSpan.FromSeconds(Math.Max(0, phone.ResendCooldownSeconds));
        var dailyCap = Math.Max(1, phone.MaxSendsPerDay);
        var phoneLifetime = TimeSpan.FromMinutes(Math.Max(1, phone.CodeExpirationMinutes));
        var phoneAttempts = Math.Max(1, phone.MaxVerificationAttempts);

        return purpose switch
        {
            OtpPurpose.PhoneVerification or
            OtpPurpose.OnboardingPhoneVerification or
            OtpPurpose.EmailVerification or
            OtpPurpose.TwoFactorLogin or
            OtpPurpose.AccountDeletion => new OtpPolicy(codeLength, phoneLifetime, phoneAttempts, cooldown, dailyCap),
            OtpPurpose.PasswordReset => new OtpPolicy(
                codeLength,
                TimeSpan.FromMinutes(Math.Max(1, resetOptions.Value.OtpExpiryMinutes)),
                Math.Max(1, resetOptions.Value.MaxOtpAttempts),
                cooldown,
                dailyCap),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };
    }

    private sealed record OtpPolicy(
        int CodeLength, TimeSpan Lifetime, int MaxAttempts, TimeSpan ResendCooldown, int MaxSendsPerDay);
}
