using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Users.Features.UnlockUser;

public class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
            
        RuleFor(x => x.UnlockedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
    }
}

public class UnlockUserHandler(
    UserManager<ApplicationUser> userManager,
    IPublisher publisher,
    ITokenBlacklistService tokenBlacklistService,
    ILogger<UnlockUserHandler> logger)
    : ICommandHandler<UnlockUserCommand, UnlockUserResult>
{
    public async Task<UnlockUserResult> Handle(
        UnlockUserCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Unlocking user {UserId} by admin {AdminId}",
            command.UserId, command.UnlockedByAdminId);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found for unlocking: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Check if user is locked
        var isLocked = await userManager.IsLockedOutAsync(user);
        if (!isLocked)
        {
            logger.LogWarning("User {UserId} is not locked", command.UserId);
            throw new InvalidUserOperationException(
                $"User '{user.UserName}' is not currently locked");
        }

        // Validate admin exists
        var admin = await userManager.FindByIdAsync(command.UnlockedByAdminId.ToString());
        if (admin == null)
        {
            throw new UserNotFoundException(command.UnlockedByAdminId);
        }

        // Remove lockout
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        
        // Reactivate user
        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Remove user-level Redis blacklist so fresh login is possible immediately
        await tokenBlacklistService.RemoveUserBlacklistAsync(command.UserId);

        // Raise domain event
        await publisher.Publish(new UserUnlockedEvent
        {
            UserId = user.Id,
            UnlockReason = command.UnlockReason,
            UnlockedBy = command.UnlockedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} unlocked successfully by admin {AdminId}",
            command.UserId, command.UnlockedByAdminId);

        var message = string.IsNullOrEmpty(command.UnlockReason)
            ? "User account unlocked successfully"
            : $"User account unlocked. Reason: {command.UnlockReason}";

        return new UnlockUserResult(true, message);
    }
}
