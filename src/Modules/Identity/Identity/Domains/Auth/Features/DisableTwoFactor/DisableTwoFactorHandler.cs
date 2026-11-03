using Identity.Contracts.Auth.Commands;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.DisableTwoFactor;

public class DisableTwoFactorCommandValidator : AbstractValidator<DisableTwoFactorCommand>
{
    public DisableTwoFactorCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().WithMessage("The password is required");
        RuleFor(x => x.Code).NotEmpty().WithMessage("An authenticator or recovery code is required");
    }
}

/// <summary>
/// Turns 2FA off for the caller. Needs BOTH the password and a second factor (authenticator code or recovery code),
/// so a stolen access token alone cannot strip 2FA. Wrong password/code count towards lockout.
/// Removes the key and recovery codes, revokes every session and returns a fresh (non-MFA) one.
/// </summary>
public class DisableTwoFactorHandler(
    UserManager<ApplicationUser> userManager,
    ITwoFactorService twoFactor,
    ISessionRevoker sessionRevoker,
    IAuthSessionService sessionService,
    IIdentityAuditWriter audit,
    IIdentityUnitOfWork unitOfWork,
    IHttpContextService httpContextService,
    ILogger<DisableTwoFactorHandler> logger)
    : ICommandHandler<DisableTwoFactorCommand, DisableTwoFactorResult>
{
    public const string InvalidCredentialsMessage = "The password or the code is incorrect.";

    public async Task<DisableTwoFactorResult> Handle(DisableTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is disabled.");
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            throw new BadRequestException("Two-factor authentication is not enabled.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException(TwoFactorService.LockedOutMessage);
        }

        if (!await userManager.CheckPasswordAsync(user, command.Password))
        {
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Two-factor disable rejected for user {UserId}: wrong password", user.Id);
            throw new BadRequestException(InvalidCredentialsMessage);
        }

        var factor = await twoFactor.VerifySecondFactorAsync(
            user, command.Code, recoveryCode: command.Code, allowRecoveryCode: true);
        if (factor == SecondFactorResult.Failed)
        {
            throw new BadRequestException(InvalidCredentialsMessage);
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserAuditSnapshot.Of(user, roles);

        // Credentials are verified (strikes already persisted); the change + audit are one unit of work.
        var session = await unitOfWork.ExecuteAsync(async () =>
        {
            await twoFactor.RemoveAsync(user);
            await sessionRevoker.RevokeAllAsync(user.Id, "Two-factor authentication disabled", cancellationToken);
            user = await userManager.FindByIdAsync(user.Id.ToString()) ?? user;

            audit.Record(IdentityAuditActions.TwoFactorDisabled, user.Id,
                before: before, after: UserAuditSnapshot.Of(user, roles), actorId: user.Id);

            return await sessionService.IssueAsync(user, httpContextService.GetIpAddress(), cancellationToken);
        }, cancellationToken);

        logger.LogInformation("User {UserId} disabled two-factor authentication", user.Id);

        return new DisableTwoFactorResult(
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt,
            "Two-factor authentication disabled. All other sessions were signed out.");
    }
}
