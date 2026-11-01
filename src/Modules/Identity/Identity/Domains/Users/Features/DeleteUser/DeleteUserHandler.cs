using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

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
    IPublisher publisher,
    ILogger<DeleteUserHandler> logger)
    : ICommandHandler<DeleteUserCommand, DeleteUserResult>
{
    public async Task<DeleteUserResult> Handle(
        DeleteUserCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Deleting user {UserId} by admin {AdminId}. Reason: {Reason}",
            command.UserId, command.DeletedByAdminId, command.Reason);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found for deletion: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Prevent deleting admins through this endpoint
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains("Admin") || roles.Contains("SuperAdmin"))
        {
            logger.LogWarning(
                "Attempt to delete admin user {UserId} blocked",
                command.UserId);
            throw new InvalidUserOperationException(
                "Cannot delete admin users through this endpoint. Use admin management features.");
        }

        // Validate admin exists
        var admin = await userManager.FindByIdAsync(command.DeletedByAdminId.ToString());
        if (admin == null)
        {
            throw new UserNotFoundException(command.DeletedByAdminId);
        }

        // Soft delete: lock permanently and deactivate
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Raise domain event
        await publisher.Publish(new UserDeletedEvent
        {
            UserId = user.Id,
            Reason = command.Reason,
            DeletedBy = command.DeletedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} deleted successfully by admin {AdminId}",
            command.UserId, command.DeletedByAdminId);

        return new DeleteUserResult(
            true,
            $"User account deleted successfully. Reason: {command.Reason}");
    }
}
