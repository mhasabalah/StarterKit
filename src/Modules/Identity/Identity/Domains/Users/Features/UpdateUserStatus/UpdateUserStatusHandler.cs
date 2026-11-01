using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

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
    IPublisher publisher,
    ILogger<UpdateUserStatusHandler> logger)
    : ICommandHandler<UpdateUserStatusCommand, UpdateUserStatusResult>
{
    public async Task<UpdateUserStatusResult> Handle(
        UpdateUserStatusCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Updating user {UserId} status to {IsActive} by admin {AdminId}",
            command.UserId, command.IsActive, command.UpdatedByAdminId);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Check if status is already set
        if (user.IsActive == command.IsActive)
        {
            logger.LogWarning(
                "User {UserId} already has status IsActive={IsActive}",
                command.UserId, command.IsActive);
            throw new InvalidUserOperationException(
                $"User is already {(command.IsActive ? "active" : "inactive")}");
        }

        // Validate admin exists
        var admin = await userManager.FindByIdAsync(command.UpdatedByAdminId.ToString());
        if (admin == null)
        {
            throw new UserNotFoundException(command.UpdatedByAdminId);
        }

        // Update status
        user.IsActive = command.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Raise domain event
        await publisher.Publish(new UserStatusUpdatedEvent
        {
            UserId = user.Id,
            IsActive = command.IsActive,
            Reason = command.Reason,
            UpdatedBy = command.UpdatedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} status updated successfully to {IsActive}",
            command.UserId, command.IsActive);

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
