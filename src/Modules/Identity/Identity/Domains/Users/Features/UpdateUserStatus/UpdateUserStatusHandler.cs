using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

namespace Identity.Domains.Users.Features.UpdateUserStatus;

public class UpdateUserStatusCommandValidator : AbstractValidator<UpdateUserStatusCommand>
{
    public UpdateUserStatusCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        RuleFor(x => x.UpdatedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class UpdateUserStatusHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<UpdateUserStatusHandler> logger)
    : ICommandHandler<UpdateUserStatusCommand, UpdateUserStatusResult>
{
    public async Task<UpdateUserStatusResult> Handle(
        UpdateUserStatusCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureCanManageAsync(command.UpdatedByAdminId, user, cancellationToken);

        if (user.IsActive == command.IsActive)
        {
            throw new InvalidUserOperationException(
                $"User is already {(command.IsActive ? "active" : "inactive")}");
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserAuditSnapshot.Of(user, roles);

        user.IsActive = command.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        if (!command.IsActive)
        {
            await sessionRevoker.RevokeAllAsync(user.Id, "Account deactivated by admin", cancellationToken);
        }

        audit.Record(IdentityAuditActions.UserStatusChanged, user.Id, reason: command.Reason,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: command.UpdatedByAdminId);

        await publisher.Publish(new UserStatusUpdatedEvent
        {
            UserId = user.Id,
            IsActive = command.IsActive,
            Reason = command.Reason,
            UpdatedBy = command.UpdatedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} status set to IsActive={IsActive} by admin {AdminId}",
            user.Id, command.IsActive, command.UpdatedByAdminId);

        var message = command.IsActive
            ? "User account activated successfully"
            : "User account deactivated successfully";

        if (!string.IsNullOrEmpty(command.Reason))
        {
            message += $". Reason: {command.Reason}";
        }

        return new UpdateUserStatusResult(true, message, command.IsActive);
    }
}
