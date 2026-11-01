using Identity.Contracts.Auth.Commands;
using Identity.Resources;
using Identity.Services.Token;
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
            .Length(6).WithMessage(_ => localizer["Validation.Code.Length"]);
    }
}

public class VerifyPhoneHandler(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator tokenGenerator,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<VerifyPhoneHandler> logger)
    : ICommandHandler<VerifyPhoneCommand, VerifyPhoneResult>
{
    public async Task<VerifyPhoneResult> Handle(
        VerifyPhoneCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());

        if (user == null)
        {
            throw new NotFoundException("User", command.UserId);
        }

        if (user.IsPhoneNumberVerified)
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.AlreadyVerified"]);
        }

        if (string.IsNullOrEmpty(user.PhoneVerificationCode))
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.NoCode"]);
        }

        if (user.PhoneVerificationCodeExpiry < DateTime.UtcNow)
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.Expired"]);
        }

        if (user.PhoneVerificationCode != command.VerificationCode)
        {
            throw new BadRequestException(localizer["Auth.PhoneVerification.InvalidCode"]);
        }

        // Mark phone as verified
        user.IsPhoneNumberVerified = true;
        user.PhoneVerificationCode = null;
        user.PhoneVerificationCodeExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;

        // Update profile completion status
        user.UpdateProfileCompletion();

        await userManager.UpdateAsync(user);

        // Generate tokens
        var accessToken = await tokenGenerator.GenerateTokenAsync(user);
        var refreshToken = tokenGenerator.GenerateRefreshToken();

        logger.LogInformation("User {UserId} phone verified successfully", command.UserId);

        return new VerifyPhoneResult(
            accessToken,
            refreshToken,
            DateTime.UtcNow.AddHours(1),
            localizer["Auth.PhoneVerification.Success"]
        );
    }
}
