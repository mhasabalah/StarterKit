using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

namespace Identity.Domains.Admins.Features.RevokeRole;

public class RevokeRoleCommandValidator : AbstractValidator<RevokeRoleCommand>
{
    public RevokeRoleCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        RuleFor(x => x.RoleName)
            .NotEmpty()
            .Must(role => RoleConstants.GetAllRoles().Contains(role))
            .WithMessage("Invalid role name");

        RuleFor(x => x.RevokedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class RevokeRoleHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<RevokeRoleHandler> logger)
    : ICommandHandler<RevokeRoleCommand, RevokeRoleResult>
{
    public async Task<RevokeRoleResult> Handle(RevokeRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        // Also blocks self-revocation (callers cannot manage themselves).
        await roleHierarchyGuard.EnsureCanManageAsync(command.RevokedByAdminId, user, cancellationToken);

        if (RoleConstants.IsStaffRole(command.RoleName))
        {
            await roleHierarchyGuard.EnsureIsSuperAdminAsync(command.RevokedByAdminId, cancellationToken);
        }

        if (!await userManager.IsInRoleAsync(user, command.RoleName))
        {
            throw new InvalidRoleOperationException($"User does not have the '{command.RoleName}' role");
        }

        var before = UserAuditSnapshot.Of(user, await userManager.GetRolesAsync(user));

        var result = await userManager.RemoveFromRoleAsync(user, command.RoleName);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError("Failed to revoke role {Role} from user {UserId}: {Errors}", command.RoleName, user.Id, errors);
            return new RevokeRoleResult(false, $"Failed to revoke role: {errors}", command.RoleName);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Tokens still carry the revoked role claim: end every session (also rotates the security stamp).
        await sessionRevoker.RevokeAllAsync(user.Id, $"Role '{command.RoleName}' revoked", cancellationToken);

        audit.Record(IdentityAuditActions.RoleRevoked, user.Id, reason: command.Reason,
            before: before, after: UserAuditSnapshot.Of(user, await userManager.GetRolesAsync(user)),
            actorId: command.RevokedByAdminId);

        await publisher.Publish(new RoleRevokedEvent
        {
            UserId = user.Id,
            RoleName = command.RoleName,
            Reason = command.Reason,
            RevokedBy = command.RevokedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Role {Role} revoked from user {UserId} by {AdminId}", command.RoleName, user.Id, command.RevokedByAdminId);

        var message = string.IsNullOrEmpty(command.Reason)
            ? $"Role '{command.RoleName}' revoked successfully"
            : $"Role '{command.RoleName}' revoked successfully. Reason: {command.Reason}";

        return new RevokeRoleResult(true, message, command.RoleName);
    }
}
