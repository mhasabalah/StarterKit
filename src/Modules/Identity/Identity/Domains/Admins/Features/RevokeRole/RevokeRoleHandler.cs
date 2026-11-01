using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

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
    IPublisher publisher,
    ILogger<RevokeRoleHandler> logger)
    : ICommandHandler<RevokeRoleCommand, RevokeRoleResult>
{
    public async Task<RevokeRoleResult> Handle(
        RevokeRoleCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Revoking role {Role} from user {UserId} by admin {AdminId}",
            command.RoleName, command.UserId, command.RevokedByAdminId);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Validate admin exists and has permission
        var admin = await userManager.FindByIdAsync(command.RevokedByAdminId.ToString());
        if (admin == null)
        {
            throw new UserNotFoundException(command.RevokedByAdminId);
        }

        var adminRoles = await userManager.GetRolesAsync(admin);
        
        // Only SuperAdmin can revoke Admin or SuperAdmin roles
        if ((command.RoleName == RoleConstants.Admin || command.RoleName == RoleConstants.SuperAdmin) 
            && !adminRoles.Contains(RoleConstants.SuperAdmin))
        {
            throw new InvalidRoleOperationException(
                "Only SuperAdmin can revoke Admin or SuperAdmin roles");
        }

        // Check if user has this role
        if (!await userManager.IsInRoleAsync(user, command.RoleName))
        {
            logger.LogWarning(
                "User {UserId} does not have role {RoleName}",
                command.UserId, command.RoleName);
            throw new InvalidRoleOperationException(
                $"User does not have the '{command.RoleName}' role");
        }

        // Prevent revoking own SuperAdmin role
        if (command.UserId == command.RevokedByAdminId && 
            command.RoleName == RoleConstants.SuperAdmin)
        {
            throw new InvalidRoleOperationException(
                "Cannot revoke your own SuperAdmin role");
        }

        // Revoke role
        var result = await userManager.RemoveFromRoleAsync(user, command.RoleName);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError(
                "Failed to revoke role {Role} from user {UserId}: {Errors}",
                command.RoleName, command.UserId, errors);
            return new RevokeRoleResult(false, $"Failed to revoke role: {errors}", command.RoleName);
        }

        // Update user timestamp
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Raise domain event
        await publisher.Publish(new RoleRevokedEvent
        {
            UserId = user.Id,
            RoleName = command.RoleName,
            Reason = command.Reason,
            RevokedBy = command.RevokedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Role {Role} revoked successfully from user {UserId}",
            command.RoleName, command.UserId);

        var message = string.IsNullOrEmpty(command.Reason)
            ? $"Role '{command.RoleName}' revoked successfully"
            : $"Role '{command.RoleName}' revoked successfully. Reason: {command.Reason}";

        return new RevokeRoleResult(true, message, command.RoleName);
    }
}
