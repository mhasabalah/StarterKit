using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

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
    IPublisher publisher,
    ILogger<AssignRoleHandler> logger)
    : ICommandHandler<AssignRoleCommand, AssignRoleResult>
{
    public async Task<AssignRoleResult> Handle(
        AssignRoleCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Assigning role {Role} to user {UserId} by admin {AdminId}",
            command.RoleName, command.UserId, command.AssignedByAdminId);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Validate admin exists and has permission
        var admin = await userManager.FindByIdAsync(command.AssignedByAdminId.ToString());
        if (admin == null)
        {
            throw new UserNotFoundException(command.AssignedByAdminId);
        }

        var adminRoles = await userManager.GetRolesAsync(admin);
        
        // Only SuperAdmin can assign Admin or SuperAdmin roles
        if ((command.RoleName == RoleConstants.Admin || command.RoleName == RoleConstants.SuperAdmin) 
            && !adminRoles.Contains(RoleConstants.SuperAdmin))
        {
            throw new InvalidRoleOperationException(
                "Only SuperAdmin can assign Admin or SuperAdmin roles");
        }

        // Validate role exists
        if (!await roleManager.RoleExistsAsync(command.RoleName))
        {
            logger.LogWarning("Role not found: {RoleName}", command.RoleName);
            throw new RoleNotFoundException(command.RoleName);
        }

        // Check if user already has this role
        if (await userManager.IsInRoleAsync(user, command.RoleName))
        {
            logger.LogWarning(
                "User {UserId} already has role {RoleName}",
                command.UserId, command.RoleName);
            throw new InvalidRoleOperationException(
                $"User already has the '{command.RoleName}' role");
        }

        // Assign role
        var result = await userManager.AddToRoleAsync(user, command.RoleName);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError(
                "Failed to assign role {Role} to user {UserId}: {Errors}",
                command.RoleName, command.UserId, errors);
            return new AssignRoleResult(false, $"Failed to assign role: {errors}", command.RoleName);
        }

        // Update user timestamp
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Raise domain event
        await publisher.Publish(new RoleAssignedEvent
        {
            UserId = user.Id,
            RoleName = command.RoleName,
            Reason = command.Reason,
            AssignedBy = command.AssignedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Role {Role} assigned successfully to user {UserId}",
            command.RoleName, command.UserId);

        var message = string.IsNullOrEmpty(command.Reason)
            ? $"Role '{command.RoleName}' assigned successfully"
            : $"Role '{command.RoleName}' assigned successfully. Reason: {command.Reason}";

        return new AssignRoleResult(true, message, command.RoleName);
    }
}
