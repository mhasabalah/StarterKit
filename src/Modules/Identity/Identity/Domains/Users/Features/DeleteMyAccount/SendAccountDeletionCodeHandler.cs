using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Users.Commands;
using Identity.Resources;
using Identity.Services.Otp;
using Microsoft.Extensions.Localization;

namespace Identity.Domains.Users.Features.DeleteMyAccount;

public class SendAccountDeletionCodeCommandValidator : AbstractValidator<SendAccountDeletionCodeCommand>
{
    public SendAccountDeletionCodeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

/// <summary>Sends the SMS re-authentication code for deleting an account that has no password.</summary>
public class SendAccountDeletionCodeHandler(
    UserManager<ApplicationUser> userManager,
    IOtpCodeService otpCodeService,
    ISmsService smsService,
    IStringLocalizer<IdentityResource> localizer)
    : ICommandHandler<SendAccountDeletionCodeCommand, SendAccountDeletionCodeResult>
{
    public async Task<SendAccountDeletionCodeResult> Handle(
        SendAccountDeletionCodeCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");
        if (!user.IsActive) throw new UnauthorizedException("Account is disabled.");

        if (await userManager.HasPasswordAsync(user))
            throw new BadRequestException(localizer["Account.Delete.CodeNotNeeded"]);

        if (string.IsNullOrEmpty(user.PhoneNumber))
            throw new BadRequestException(localizer["Auth.PhoneVerification.NoPhone"]);

        // Throws OtpRateLimitedException (429) during the resend cooldown / after the daily cap.
        var otp = await otpCodeService.IssueAsync(user.Id, OtpPurpose.AccountDeletion, cancellationToken);
        await smsService.SendVerificationCodeAsync(user.PhoneNumber, otp.Code);

        return new SendAccountDeletionCodeResult(localizer["Account.Delete.CodeSent"], otp.ExpiresAt);
    }
}
