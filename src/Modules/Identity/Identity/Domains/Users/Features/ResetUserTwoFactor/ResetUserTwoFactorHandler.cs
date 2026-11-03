using Identity.Contracts.Users.Commands;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;

namespace Identity.Domains.Users.Features.ResetUserTwoFactor;

public class ResetUserTwoFactorCommandValidator : AbstractValidator<ResetUserTwoFactorCommand>
{
    public ResetUserTwoFactorCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ResetByAdminId).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(500)
            .WithMessage("Reason must be between 10 and 500 characters");
    }
}

/// <summary>
/// SuperAdmin-only recovery for a user who lost their authenticator AND recovery codes: removes the user's 2FA
/// (key + recovery codes), ends all of their sessions and audits it (inside the same transaction, see
/// <see cref="AuditedCommandTransactionBehavior{TRequest,TResponse}"/>). The user signs in with the password and
/// must enroll again (privileged roles get <c>mfa_required</c> until they do).
/// </summary>
public class ResetUserTwoFactorHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    ITwoFactorService twoFactor,
    ISessionRevoker sessionRevoker,
    IIdentityAuditWriter audit,
    ILogger<ResetUserTwoFactorHandler> logger)
    : ICommandHandler<ResetUserTwoFactorCommand, ResetUserTwoFactorResult>
{
    public async Task<ResetUserTwoFactorResult> Handle(
        ResetUserTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureIsSuperAdminAsync(command.ResetByAdminId, cancellationToken);
        await roleHierarchyGuard.EnsureCanManageAsync(command.ResetByAdminId, user, cancellationToken);

        var hadTwoFactor = await userManager.GetTwoFactorEnabledAsync(user);
        var hadKey = !string.IsNullOrEmpty(await twoFactor.GetKeyAsync(user));
        if (!hadTwoFactor && !hadKey)
        {
            throw new InvalidUserOperationException("The user has no two-factor authentication to reset.");
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserAuditSnapshot.Of(user, roles);

        await twoFactor.RemoveAsync(user);
        await sessionRevoker.RevokeAllAsync(user.Id, "Two-factor authentication reset by SuperAdmin", cancellationToken);

        audit.Record(IdentityAuditActions.TwoFactorReset, user.Id, reason: command.Reason,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: command.ResetByAdminId);

        logger.LogWarning(
            "Two-factor authentication of user {UserId} reset by SuperAdmin {AdminId}", user.Id, command.ResetByAdminId);

        return new ResetUserTwoFactorResult(true,
            "Two-factor authentication reset. All of the user's sessions were signed out; they must enroll again.");
    }
}
