using Identity.Contracts.Auth.Commands;
using Identity.Options;
using Identity.Services.Otp;
using Identity.Services.Phone;
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
    IOtpCodeService otpCodeService,
    IPhoneNumberNormalizer phoneNormalizer,
    ILogger<VerifyResetOtpHandler> logger)
    : ICommandHandler<VerifyResetOtpCommand, VerifyResetOtpResult>
{
    public const string InvalidOtpMessage = "Invalid or expired OTP.";

    public async Task<VerifyResetOtpResult> Handle(VerifyResetOtpCommand command, CancellationToken cancellationToken)
    {
        var phone = phoneNormalizer.Normalize(command.PhoneNumber);
        var user = phone is null ? null : await userManager.FindByPhoneAsync(phone, cancellationToken);

        if (user is null || !user.IsPhoneNumberVerified)
        {
            throw new BadRequestException(InvalidOtpMessage);
        }

        var result = await otpCodeService.VerifyAsync(
            user.Id, OtpPurpose.PasswordReset, command.OtpCode, cancellationToken);

        if (!result.Succeeded)
        {
            logger.LogWarning("Reset OTP rejected for user {UserId}: {Status}", user.Id, result.Status);
            throw new BadRequestException(result.Status == OtpVerificationStatus.Locked
                ? "Too many incorrect attempts. Please request a new reset code."
                : InvalidOtpMessage);
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);

        logger.LogInformation("Reset OTP verified for user {UserId}; reset token issued", user.Id);

        return new VerifyResetOtpResult(user.Id, resetToken, "OTP verified. Proceed to set your new password.");
    }
}
