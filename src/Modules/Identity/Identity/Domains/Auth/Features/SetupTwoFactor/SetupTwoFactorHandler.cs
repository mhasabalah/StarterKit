using Identity.Contracts.Auth.Commands;
using Identity.Services.TwoFactor;

namespace Identity.Domains.Auth.Features.SetupTwoFactor;

public class SetupTwoFactorCommandValidator : AbstractValidator<SetupTwoFactorCommand>
{
    public SetupTwoFactorCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

/// <summary>
/// Step 1 of enrollment: a fresh authenticator key, stored encrypted and PENDING (TwoFactorEnabled stays false and
/// the security stamp is untouched, so the current session keeps working). Calling it again replaces the pending key.
/// </summary>
public class SetupTwoFactorHandler(
    UserManager<ApplicationUser> userManager,
    ITwoFactorService twoFactor,
    ILogger<SetupTwoFactorHandler> logger)
    : ICommandHandler<SetupTwoFactorCommand, SetupTwoFactorResult>
{
    public async Task<SetupTwoFactorResult> Handle(SetupTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is disabled.");
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            throw new ConflictException(
                "Two-factor authentication is already enabled. Disable it first to enroll a new authenticator.");
        }

        var key = await twoFactor.CreatePendingKeyAsync(user);

        logger.LogInformation("User {UserId} started two-factor enrollment", user.Id);

        return new SetupTwoFactorResult(twoFactor.FormatKey(key), twoFactor.BuildAuthenticatorUri(user, key));
    }
}
