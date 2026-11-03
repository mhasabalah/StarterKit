using Identity.Contracts.Auth.Commands;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.EnableTwoFactor;

public class EnableTwoFactorCommandValidator : AbstractValidator<EnableTwoFactorCommand>
{
    public EnableTwoFactorCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().WithMessage("The authenticator code is required");
    }
}

/// <summary>
/// Step 2 of enrollment: proves the authenticator works, enables 2FA and issues recovery codes (stored hashed).
/// Every existing session is revoked (none of them passed 2FA); this device gets a new MFA session.
/// </summary>
public class EnableTwoFactorHandler(
    UserManager<ApplicationUser> userManager,
    ITwoFactorService twoFactor,
    ISessionRevoker sessionRevoker,
    IAuthSessionService sessionService,
    IIdentityAuditWriter audit,
    IHttpContextService httpContextService,
    ILogger<EnableTwoFactorHandler> logger)
    : ICommandHandler<EnableTwoFactorCommand, EnableTwoFactorResult>
{
    public const string InvalidCodeMessage = "The authenticator code is invalid or expired.";

    public async Task<EnableTwoFactorResult> Handle(EnableTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is disabled.");
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            throw new ConflictException("Two-factor authentication is already enabled.");
        }

        if (string.IsNullOrEmpty(await twoFactor.GetKeyAsync(user)))
        {
            throw new BadRequestException("Start enrollment first (POST /api/auth/2fa/setup).");
        }

        // Enrollment protects nothing yet, so a typo is a 400 (rate-limited), not a lockout strike.
        if (!await twoFactor.VerifyAuthenticatorCodeAsync(user, command.Code))
        {
            logger.LogWarning("Two-factor enrollment code rejected for user {UserId}", user.Id);
            throw new BadRequestException(InvalidCodeMessage);
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserAuditSnapshot.Of(user, roles);

        var enable = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enable.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to enable two-factor authentication: {string.Join(", ", enable.Errors.Select(e => e.Code))}");
        }

        var recoveryCodes = await twoFactor.GenerateRecoveryCodesAsync(user);

        await sessionRevoker.RevokeAllAsync(user.Id, "Two-factor authentication enabled", cancellationToken);
        user = await userManager.FindByIdAsync(user.Id.ToString()) ?? user;
        var session = await sessionService.IssueAsync(
            user, httpContextService.GetIpAddress(), multiFactor: true, cancellationToken);

        audit.Record(IdentityAuditActions.TwoFactorEnabled, user.Id,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: user.Id);

        logger.LogInformation("User {UserId} enabled two-factor authentication", user.Id);

        return new EnableTwoFactorResult(
            recoveryCodes,
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt,
            "Two-factor authentication enabled. Store the recovery codes safely: they are shown only once. " +
            "All other sessions were signed out.");
    }
}
