using Identity.Contracts.Auth.Commands;
using Identity.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Shared.Core.Phone;

namespace Identity.Domains.Auth.Features.VerifyResetOtp;

public class VerifyResetOtpCommandValidator : AbstractValidator<VerifyResetOtpCommand>
{
    public VerifyResetOtpCommandValidator(
        IPhoneValidationService phoneService,
        IOptions<PhoneVerificationOptions> phoneOptions)
    {
        var codeLength = phoneOptions.Value.CodeLength;

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required")
            .Must(p => phoneService.Validate(p, "ZZ").IsValid)
            .WithMessage("Must be a valid international phone number (+country_code number)");

        RuleFor(x => x.OtpCode)
            .NotEmpty().WithMessage("OTP code is required")
            .Matches($@"^\d{{{codeLength}}}$")
            .WithMessage($"OTP must be exactly {codeLength} digits");
    }
}

public class VerifyResetOtpHandler(
    UserManager<ApplicationUser> userManager,
    IDistributedCache cache,
    IOptions<PasswordResetOptions> options,
    ILogger<VerifyResetOtpHandler> logger)
    : ICommandHandler<VerifyResetOtpCommand, VerifyResetOtpResult>
{
    private readonly PasswordResetOptions _options = options.Value;

    public async Task<VerifyResetOtpResult> Handle(
        VerifyResetOtpCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == command.PhoneNumber, cancellationToken);

        if (user is null)
            throw new BadRequestException("Invalid or expired OTP.");

        var attemptKey = $"reset-otp-attempts:{user.Id}";
        var attemptsRaw = await cache.GetStringAsync(attemptKey, cancellationToken);
        var attempts = int.TryParse(attemptsRaw, out var parsed) ? parsed : 0;

        if (attempts >= _options.MaxOtpAttempts)
        {
            // Clear OTP from user record — they must request a new one
            user.PhoneVerificationCode = null;
            user.PhoneVerificationCodeExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
            await cache.RemoveAsync(attemptKey, cancellationToken);

            logger.LogWarning("Max OTP attempts reached for user {UserId}", user.Id);
            throw new BadRequestException("Too many incorrect attempts. Please request a new reset code.");
        }

        // Treat a null expiry as already expired — a null value means the OTP was never
        // properly set or was partially written; do not allow it to pass as "unexpired".
        // (null < DateTime.UtcNow is false in C#, so without this guard a null expiry
        // would silently pass the original check and be treated as a valid OTP.)
        if (string.IsNullOrEmpty(user.PhoneVerificationCode)
            || user.PhoneVerificationCodeExpiry is null
            || user.PhoneVerificationCodeExpiry < DateTime.UtcNow)
        {
            throw new BadRequestException("Invalid or expired OTP.");
        }

        if (user.PhoneVerificationCode != command.OtpCode)
        {
            // Increment attempt counter; TTL mirrors the OTP expiry so the counter
            // auto-clears when the OTP window closes.
            var newAttempts = attempts + 1;
            var cacheOptions = new DistributedCacheEntryOptions
            {
                // PhoneVerificationCodeExpiry is non-null here (checked above).
                // Cap at 10 minutes as a hard upper bound so the counter never persists
                // indefinitely if the expiry value is unexpectedly far in the future.
                AbsoluteExpiration = user.PhoneVerificationCodeExpiry < DateTimeOffset.UtcNow.AddMinutes(10)
                    ? (DateTimeOffset?)user.PhoneVerificationCodeExpiry.Value
                    : DateTimeOffset.UtcNow.AddMinutes(10)
            };
            await cache.SetStringAsync(
                attemptKey,
                newAttempts.ToString(),
                cacheOptions,
                cancellationToken);

            logger.LogWarning("Invalid OTP attempt {Attempt}/{Max} for user {UserId}",
                newAttempts, _options.MaxOtpAttempts, user.Id);
            throw new BadRequestException("Invalid or expired OTP.");
        }

        // OTP correct — clear fields and issue Identity reset token
        user.PhoneVerificationCode = null;
        user.PhoneVerificationCodeExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await cache.RemoveAsync(attemptKey, cancellationToken);

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);

        logger.LogInformation(
            "OTP verified for user {UserId}. Identity reset token issued.", user.Id);

        return new VerifyResetOtpResult(
            user.Id,
            resetToken,
            "OTP verified. Proceed to set your new password.");
    }
}
