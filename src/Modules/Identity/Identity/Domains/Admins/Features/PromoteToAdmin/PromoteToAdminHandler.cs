using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Admins.Features.PromoteToAdmin;

public class PromoteToAdminCommandValidator : AbstractValidator<PromoteToAdminCommand>
{
    public PromoteToAdminCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
            
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(500)
            .WithMessage("Reason must be between 10 and 500 characters");
            
        RuleFor(x => x.PromotedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class PromoteToAdminHandler(
    UserManager<ApplicationUser> userManager,
    IPublisher publisher,
    ILogger<PromoteToAdminHandler> logger)
    : ICommandHandler<PromoteToAdminCommand, PromoteToAdminResult>
{
    public async Task<PromoteToAdminResult> Handle(
        PromoteToAdminCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Promoting user {UserId} to {AdminType} by {PromotedBy}",
            command.UserId,
            command.MakeSuperAdmin ? "SuperAdmin" : "Admin",
            command.PromotedByAdminId);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Validate promoter exists and is SuperAdmin
        var promoter = await userManager.FindByIdAsync(command.PromotedByAdminId.ToString());
        if (promoter == null)
        {
            throw new UserNotFoundException(command.PromotedByAdminId);
        }

        var promoterRoles = await userManager.GetRolesAsync(promoter);
        if (!promoterRoles.Contains(RoleConstants.SuperAdmin))
        {
            throw new InvalidUserOperationException(
                "Only SuperAdmin can promote users to admin");
        }

        // Check current roles
        var currentRoles = await userManager.GetRolesAsync(user);
        
        // Check if already admin
        if (currentRoles.Contains(RoleConstants.Admin))
        {
            if (command.MakeSuperAdmin && currentRoles.Contains(RoleConstants.SuperAdmin))
            {
                throw new InvalidUserOperationException("User is already a SuperAdmin");
            }
            if (!command.MakeSuperAdmin)
            {
                throw new InvalidUserOperationException("User is already an Admin");
            }
        }

        // Ensure User role exists
        if (!currentRoles.Contains(RoleConstants.User))
        {
            await userManager.AddToRoleAsync(user, RoleConstants.User);
        }

        // Add Admin role
        if (!currentRoles.Contains(RoleConstants.Admin))
        {
            var addAdminResult = await userManager.AddToRoleAsync(user, RoleConstants.Admin);
            if (!addAdminResult.Succeeded)
            {
                var errors = string.Join(", ", addAdminResult.Errors.Select(e => e.Description));
                return new PromoteToAdminResult(
                    false,
                    $"Failed to promote to Admin: {errors}",
                    currentRoles.ToList());
            }
        }

        // Add SuperAdmin role if requested
        if (command.MakeSuperAdmin && !currentRoles.Contains(RoleConstants.SuperAdmin))
        {
            var addSuperResult = await userManager.AddToRoleAsync(user, RoleConstants.SuperAdmin);
            if (!addSuperResult.Succeeded)
            {
                var errors = string.Join(", ", addSuperResult.Errors.Select(e => e.Description));
                return new PromoteToAdminResult(
                    false,
                    $"Failed to promote to SuperAdmin: {errors}",
                    currentRoles.ToList());
            }
        }

        // Update user timestamp
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Get updated roles
        var newRoles = await userManager.GetRolesAsync(user);

        // Raise domain event
        await publisher.Publish(new UserPromotedToAdminEvent
        {
            UserId = user.Id,
            IsSuperAdmin = command.MakeSuperAdmin,
            Reason = command.Reason,
            PromotedBy = command.PromotedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} promoted successfully to {AdminType}",
            command.UserId,
            command.MakeSuperAdmin ? "SuperAdmin" : "Admin");

        var message = command.MakeSuperAdmin
            ? $"User promoted to SuperAdmin successfully. Reason: {command.Reason}"
            : $"User promoted to Admin successfully. Reason: {command.Reason}";

        return new PromoteToAdminResult(true, message, newRoles.ToList());
    }
}
