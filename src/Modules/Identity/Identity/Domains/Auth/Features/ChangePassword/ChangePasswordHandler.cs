using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Validation;
using Identity.Services.Sessions;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required");

        RuleFor(x => x.NewPassword).ApplyPasswordRules();

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}

/// <summary>
/// Self-service password change. Every other session (all refresh tokens and access tokens) is revoked;
/// the caller gets a brand-new session so this device stays signed in.
/// Clears MustChangePasswordOnNextLogin (e.g. the seeded SuperAdmin).
/// </summary>
public class ChangePasswordHandler(
    UserManager<ApplicationUser> userManager,
    ISessionRevoker sessionRevoker,
    IAuthSessionService sessionService,
    IHttpContextService httpContextService,
    ILogger<ChangePasswordHandler> logger)
    : ICommandHandler<ChangePasswordCommand, ChangePasswordResult>
{
    public async Task<ChangePasswordResult> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is disabled.");
        }

        var result = await userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!result.Succeeded)
        {
            logger.LogWarning("Password change failed for user {UserId}", user.Id);
            throw new BadRequestException(
                result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                    ? "The current password is incorrect."
                    : string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        if (user.MustChangePasswordOnNextLogin)
        {
            user.MustChangePasswordOnNextLogin = false;
            await userManager.UpdateAsync(user);
        }

        await sessionRevoker.RevokeAllAsync(user.Id, "Password changed", cancellationToken);

        // The caller already proved the second factor for this session; the replacement session keeps that.
        var multiFactor = AuthenticationMethods.IsMultiFactor(httpContextService.GetAllClaims());
        var session = await sessionService.IssueAsync(
            user, httpContextService.GetIpAddress(), multiFactor, cancellationToken);

        logger.LogInformation("User {UserId} changed their password", user.Id);

        return new ChangePasswordResult(
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt,
            "Password changed. All other sessions were signed out.");
    }
}
