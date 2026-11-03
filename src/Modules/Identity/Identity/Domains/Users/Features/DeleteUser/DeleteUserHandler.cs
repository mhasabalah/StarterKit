using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

namespace Identity.Domains.Users.Features.DeleteUser;

public class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(500)
            .WithMessage("Deletion reason must be between 10 and 500 characters");

        RuleFor(x => x.DeletedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class DeleteUserHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<DeleteUserHandler> logger)
    : ICommandHandler<DeleteUserCommand, DeleteUserResult>
{
    public async Task<DeleteUserResult> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureCanManageAsync(command.DeletedByAdminId, user, cancellationToken);

        var roles = await userManager.GetRolesAsync(user);
        if (RoleHierarchy.IsAdminOrAbove(roles))
        {
            throw new InvalidUserOperationException(
                "Cannot delete admin users through this endpoint. Demote the admin first.");
        }

        var before = UserAuditSnapshot.Of(user, roles);

        // Soft delete: lock permanently and deactivate, then end every session.
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await sessionRevoker.RevokeAllAsync(user.Id, "Account deleted by admin", cancellationToken);

        audit.Record(IdentityAuditActions.UserDeleted, user.Id, reason: command.Reason,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: command.DeletedByAdminId);

        await publisher.Publish(new UserDeletedEvent
        {
            UserId = user.Id,
            Reason = command.Reason,
            DeletedBy = command.DeletedByAdminId
        }, cancellationToken);

        logger.LogInformation("User {UserId} deleted by admin {AdminId}", user.Id, command.DeletedByAdminId);

        return new DeleteUserResult(true, $"User account deleted successfully. Reason: {command.Reason}");
    }
}
