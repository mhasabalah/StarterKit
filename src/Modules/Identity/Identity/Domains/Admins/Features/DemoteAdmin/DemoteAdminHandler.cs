using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Admins.Features.DemoteAdmin;

public class DemoteAdminCommandValidator : AbstractValidator<DemoteAdminCommand>
{
    public DemoteAdminCommandValidator()
    {
        RuleFor(x => x.AdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
            
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(500)
            .WithMessage("Reason must be between 10 and 500 characters");
            
        RuleFor(x => x.DemotedByAdminId)
            .NotEmpty()
            .WithMessage("Demoter admin ID is required");
    }
}

public class DemoteAdminHandler(
    UserManager<ApplicationUser> userManager,
    IPublisher publisher,
    ILogger<DemoteAdminHandler> logger)
    : ICommandHandler<DemoteAdminCommand, DemoteAdminResult>
{
    public async Task<DemoteAdminResult> Handle(
        DemoteAdminCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Demoting admin {AdminId} by {DemotedBy}",
            command.AdminId, command.DemotedByAdminId);

        // Validate admin exists
        var admin = await userManager.FindByIdAsync(command.AdminId.ToString());
        if (admin == null)
        {
            logger.LogWarning("Admin not found: {AdminId}", command.AdminId);
            throw new AdminNotFoundException(command.AdminId);
        }

        // Validate demoter exists and is SuperAdmin
        var demoter = await userManager.FindByIdAsync(command.DemotedByAdminId.ToString());
        if (demoter == null)
        {
            throw new UserNotFoundException(command.DemotedByAdminId);
        }

        var demoterRoles = await userManager.GetRolesAsync(demoter);
        if (!demoterRoles.Contains(RoleConstants.SuperAdmin))
        {
            throw new InvalidUserOperationException(
                "Only SuperAdmin can demote admins");
        }

        // Prevent self-demotion
        if (command.AdminId == command.DemotedByAdminId)
        {
            throw new InvalidUserOperationException(
                "Cannot demote yourself");
        }

        // Check current roles
        var currentRoles = await userManager.GetRolesAsync(admin);
        
        // Verify user is actually an admin
        if (!currentRoles.Contains(RoleConstants.Admin) && 
            !currentRoles.Contains(RoleConstants.SuperAdmin))
        {
            throw new AdminNotFoundException(command.AdminId);
        }

        // Remove Admin role
        if (currentRoles.Contains(RoleConstants.Admin))
        {
            var removeAdminResult = await userManager.RemoveFromRoleAsync(admin, RoleConstants.Admin);
            if (!removeAdminResult.Succeeded)
            {
                var errors = string.Join(", ", removeAdminResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to remove Admin role: {Errors}", errors);
            }
        }

        // Remove SuperAdmin role
        if (currentRoles.Contains(RoleConstants.SuperAdmin))
        {
            var removeSuperResult = await userManager.RemoveFromRoleAsync(admin, RoleConstants.SuperAdmin);
            if (!removeSuperResult.Succeeded)
            {
                var errors = string.Join(", ", removeSuperResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to remove SuperAdmin role: {Errors}", errors);
            }
        }

        // Update user timestamp
        admin.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(admin);

        // Get remaining roles
        var remainingRoles = await userManager.GetRolesAsync(admin);

        // Raise domain event
        await publisher.Publish(new AdminDemotedEvent
        {
            AdminId = admin.Id,
            Reason = command.Reason,
            DemotedBy = command.DemotedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Admin {AdminId} demoted successfully to regular user",
            command.AdminId);

        return new DemoteAdminResult(
            true,
            $"Admin demoted to regular user successfully. Reason: {command.Reason}",
            remainingRoles.ToList());
    }
}
