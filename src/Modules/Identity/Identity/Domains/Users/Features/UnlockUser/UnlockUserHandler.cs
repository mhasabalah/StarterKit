using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;

namespace Identity.Domains.Users.Features.UnlockUser;

public class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        RuleFor(x => x.UnlockedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class UnlockUserHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<UnlockUserHandler> logger)
    : ICommandHandler<UnlockUserCommand, UnlockUserResult>
{
    public async Task<UnlockUserResult> Handle(UnlockUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureCanManageAsync(command.UnlockedByAdminId, user, cancellationToken);

        if (!await userManager.IsLockedOutAsync(user))
        {
            throw new InvalidUserOperationException("User is not currently locked");
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserAuditSnapshot.Of(user, roles);

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        // Old sessions stay dead (the lock rotated the security stamp); the user simply signs in again.
        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        audit.Record(IdentityAuditActions.UserUnlocked, user.Id, reason: command.UnlockReason,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: command.UnlockedByAdminId);

        await publisher.Publish(new UserUnlockedEvent
        {
            UserId = user.Id,
            UnlockReason = command.UnlockReason,
            UnlockedBy = command.UnlockedByAdminId
        }, cancellationToken);

        logger.LogInformation("User {UserId} unlocked by admin {AdminId}", user.Id, command.UnlockedByAdminId);

        var message = string.IsNullOrEmpty(command.UnlockReason)
            ? "User account unlocked successfully"
            : $"User account unlocked. Reason: {command.UnlockReason}";

        return new UnlockUserResult(true, message);
    }
}
