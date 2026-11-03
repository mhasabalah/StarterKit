using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

namespace Identity.Domains.Admins.Features.AssignRole;

public class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");

        RuleFor(x => x.RoleName)
            .NotEmpty()
            .Must(role => RoleConstants.GetAllRoles().Contains(role))
            .WithMessage("Invalid role name");

        RuleFor(x => x.AssignedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class AssignRoleHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<AssignRoleHandler> logger)
    : ICommandHandler<AssignRoleCommand, AssignRoleResult>
{
    public async Task<AssignRoleResult> Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureCanManageAsync(command.AssignedByAdminId, user, cancellationToken);

        // Granting a staff role (Admin, SuperAdmin, Finance, Support) is a SuperAdmin-only operation.
        if (RoleConstants.IsStaffRole(command.RoleName))
        {
            await roleHierarchyGuard.EnsureIsSuperAdminAsync(command.AssignedByAdminId, cancellationToken);
        }

        if (!await roleManager.RoleExistsAsync(command.RoleName))
        {
            throw new RoleNotFoundException(command.RoleName);
        }

        if (await userManager.IsInRoleAsync(user, command.RoleName))
        {
            throw new InvalidRoleOperationException($"User already has the '{command.RoleName}' role");
        }

        var before = UserAuditSnapshot.Of(user, await userManager.GetRolesAsync(user));

        var result = await userManager.AddToRoleAsync(user, command.RoleName);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError("Failed to assign role {Role} to user {UserId}: {Errors}", command.RoleName, user.Id, errors);
            return new AssignRoleResult(false, $"Failed to assign role: {errors}", command.RoleName);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Live tokens carry the old role set: end every session (rotates the security stamp) so the grant takes
        // effect now and the new, possibly MFA-gated, privileges start from a fresh login.
        await sessionRevoker.RevokeAllAsync(user.Id, $"Role '{command.RoleName}' assigned", cancellationToken);

        audit.Record(IdentityAuditActions.RoleAssigned, user.Id, reason: command.Reason,
            before: before, after: UserAuditSnapshot.Of(user, await userManager.GetRolesAsync(user)),
            actorId: command.AssignedByAdminId);

        await publisher.Publish(new RoleAssignedEvent
        {
            UserId = user.Id,
            RoleName = command.RoleName,
            Reason = command.Reason,
            AssignedBy = command.AssignedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Role {Role} assigned to user {UserId} by {AdminId}", command.RoleName, user.Id, command.AssignedByAdminId);

        var message = string.IsNullOrEmpty(command.Reason)
            ? $"Role '{command.RoleName}' assigned successfully"
            : $"Role '{command.RoleName}' assigned successfully. Reason: {command.Reason}";

        return new AssignRoleResult(true, message, command.RoleName);
    }
}
