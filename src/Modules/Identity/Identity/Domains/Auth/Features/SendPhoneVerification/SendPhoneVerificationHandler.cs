using Identity.Contracts.Auth.Commands;
using Identity.Resources;
using Identity.Services.Otp;
using Microsoft.Extensions.Localization;

namespace Identity.Domains.Auth.Features.SendPhoneVerification;

public class SendPhoneVerificationCommandValidator : AbstractValidator<SendPhoneVerificationCommand>
{
    public SendPhoneVerificationCommandValidator(IStringLocalizer<IdentityResource> localizer)
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage(_ => localizer["Validation.UserId.Required"]);
    }
}

public class SendPhoneVerificationHandler(
    UserManager<ApplicationUser> userManager,
    IOtpCodeService otpCodeService,
    ISmsService smsService,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<SendPhoneVerificationHandler> logger)
    : ICommandHandler<SendPhoneVerificationCommand, SendPhoneVerificationResult>
{
    public async Task<SendPhoneVerificationResult> Handle(
        SendPhoneVerificationCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new NotFoundException("User", command.UserId);

        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.NoPhone"]);
        }

        if (user.IsPhoneNumberVerified)
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.AlreadyVerified"]);
        }

        // Throws OtpRateLimitedException (429) during the resend cooldown / after the daily cap.
        var otp = await otpCodeService.IssueAsync(user.Id, OtpPurpose.PhoneVerification, cancellationToken);
        await smsService.SendVerificationCodeAsync(user.PhoneNumber, otp.Code);

        logger.LogInformation("Phone verification code sent to user {UserId}", user.Id);

        return new SendPhoneVerificationResult(localizer["Auth.PhoneVerification.Sent"], otp.ExpiresAt);
    }
}
