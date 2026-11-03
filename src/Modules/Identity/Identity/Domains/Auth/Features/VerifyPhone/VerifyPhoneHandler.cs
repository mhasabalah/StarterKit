using Identity.Contracts.Auth.Commands;
using Identity.Resources;
using Identity.Services.Otp;
using Microsoft.Extensions.Localization;

namespace Identity.Domains.Auth.Features.VerifyPhone;

public class VerifyPhoneCommandValidator : AbstractValidator<VerifyPhoneCommand>
{
    public VerifyPhoneCommandValidator(IStringLocalizer<IdentityResource> localizer)
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage(_ => localizer["Validation.UserId.Required"]);

        RuleFor(x => x.VerificationCode)
            .NotEmpty().WithMessage(_ => localizer["Validation.Code.Required"])
            .Matches(@"^\d{4,9}$").WithMessage(_ => localizer["Validation.Code.Length"]);
    }
}

/// <summary>
/// Marks the signed-in user's phone as verified. Deliberately does NOT issue tokens: signing in after a phone
/// code is only part of the onboarding flow (/api/identity/onboarding/verify-phone).
/// </summary>
public class VerifyPhoneHandler(
    UserManager<ApplicationUser> userManager,
    IOtpCodeService otpCodeService,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<VerifyPhoneHandler> logger)
    : ICommandHandler<VerifyPhoneCommand, VerifyPhoneResult>
{
    public async Task<VerifyPhoneResult> Handle(VerifyPhoneCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new NotFoundException("User", command.UserId);

        if (user.IsPhoneNumberVerified)
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.AlreadyVerified"]);
        }

        var result = await otpCodeService.VerifyAsync(
            user.Id, OtpPurpose.PhoneVerification, command.VerificationCode, cancellationToken);

        if (!result.Succeeded)
        {
            throw new BadRequestException(result.Status switch
            {
                OtpVerificationStatus.NotFound => localizer["Auth.PhoneVerification.NoCode"],
                OtpVerificationStatus.Expired => localizer["Auth.PhoneVerification.Expired"],
                OtpVerificationStatus.Locked => "Too many incorrect attempts. Please request a new code.",
                _ => localizer["Auth.PhoneVerification.InvalidCode"]
            });
        }

        user.IsPhoneNumberVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdateProfileCompletion();
        await userManager.UpdateAsync(user);

        logger.LogInformation("User {UserId} phone verified", user.Id);

        return new VerifyPhoneResult(localizer["Auth.PhoneVerification.Success"], user.IsProfileComplete);
    }
}
