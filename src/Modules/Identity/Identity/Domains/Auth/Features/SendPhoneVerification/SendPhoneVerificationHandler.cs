using Identity.Contracts.Auth.Commands;
using Identity.Options;
using Identity.Resources;
using Identity.Services.Sms;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

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
    ISmsService smsService,
    IOptions<PhoneVerificationOptions> options,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<SendPhoneVerificationHandler> logger)
    : ICommandHandler<SendPhoneVerificationCommand, SendPhoneVerificationResult>
{
    private readonly PhoneVerificationOptions _options = options.Value;

    public async Task<SendPhoneVerificationResult> Handle(
        SendPhoneVerificationCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());

        if (user == null)
        {
            throw new NotFoundException("User", command.UserId);
        }

        if (string.IsNullOrEmpty(user.PhoneNumber))
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.NoPhone"]);
        }

        if (user.IsPhoneNumberVerified)
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.AlreadyVerified"]);
        }

        // Generate code based on configured length
        var min = (int)Math.Pow(10, _options.CodeLength - 1);
        var max = (int)Math.Pow(10, _options.CodeLength) - 1;
        var code = Random.Shared.Next(min, max).ToString();

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.CodeExpirationMinutes);

        user.PhoneVerificationCode = code;
        user.PhoneVerificationCodeExpiry = expiresAt;
        user.UpdatedAt = DateTime.UtcNow;

        await userManager.UpdateAsync(user);

        // Send SMS
        await smsService.SendVerificationCodeAsync(user.PhoneNumber, code);

        logger.LogInformation("Phone verification code sent to user {UserId}", command.UserId);

        return new SendPhoneVerificationResult(
            localizer["Auth.PhoneVerification.Sent"],
            expiresAt
        );
    }
}
