using Identity.Contracts.Users.Commands;
using Identity.Data;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Domains.Users.Features.LockUser;

public class LockUserCommandValidator : AbstractValidator<LockUserCommand>
{
    public LockUserCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("User ID is required");
            
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(500)
            .WithMessage("Reason must be between 10 and 500 characters");
            
        RuleFor(x => x.LockedByAdminId)
            .NotEmpty()
            .WithMessage("Admin ID is required");
            
        When(x => x.Duration == LockDuration.Custom, () =>
        {
            RuleFor(x => x.CustomDurationHours)
                .GreaterThan(0)
                .LessThanOrEqualTo(8760) // 1 year
                .WithMessage("Custom duration must be between 1 hour and 1 year");
        });
    }
}

public class LockUserHandler(
    UserManager<ApplicationUser> userManager,
    IPublisher publisher,
    ApplicationIdentityDbContext dbContext,
    ITokenBlacklistService tokenBlacklistService,
    IOptions<JwtOptions> jwtOptions,
    ILogger<LockUserHandler> logger)
    : ICommandHandler<LockUserCommand, LockUserResult>
{
    public async Task<LockUserResult> Handle(
        LockUserCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Locking user {UserId} with duration {Duration} by admin {AdminId}",
            command.UserId, command.Duration, command.LockedByAdminId);

        // Validate user exists
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
        {
            logger.LogWarning("User not found for locking: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        // Check if already locked
        var isCurrentlyLocked = await userManager.IsLockedOutAsync(user);
        if (isCurrentlyLocked)
        {
            logger.LogWarning(
                "User {UserId} is already locked until {LockoutEnd}",
                command.UserId, user.LockoutEnd);
            throw new UserAlreadyLockedException(command.UserId);
        }

        // Validate admin exists
        var admin = await userManager.FindByIdAsync(command.LockedByAdminId.ToString());
        if (admin == null)
        {
            throw new UserNotFoundException(command.LockedByAdminId);
        }

        // Calculate lockout end time
        var lockoutEnd = CalculateLockoutEnd(command.Duration, command.CustomDurationHours);

        // Enable lockout
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        
        // Deactivate user
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Revoke all active refresh tokens
        var activeTokens = await dbContext.RefreshTokens
            .Where(t => t.UserId == command.UserId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedReason = "Account locked by admin";
        }

        if (activeTokens.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);

        // Blacklist user in Redis for the access token lifetime
        var blacklistDuration = TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenExpirationMinutes + 1);
        await tokenBlacklistService.BlacklistUserAsync(command.UserId, blacklistDuration);

        // Raise domain event
        await publisher.Publish(new UserLockedEvent
        {
            UserId = user.Id,
            Reason = command.Reason,
            LockoutEnd = lockoutEnd,
            LockedBy = command.LockedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} locked successfully until {LockoutEnd} by admin {AdminId}",
            command.UserId, lockoutEnd, command.LockedByAdminId);

        var message = lockoutEnd == DateTimeOffset.MaxValue
            ? $"User account locked permanently. Reason: {command.Reason}"
            : $"User account locked until {lockoutEnd:yyyy-MM-dd HH:mm:ss UTC}. Reason: {command.Reason}";

        return new LockUserResult(true, message, lockoutEnd);
    }

    private static DateTimeOffset CalculateLockoutEnd(
        LockDuration duration,
        int? customHours)
    {
        return duration switch
        {
            LockDuration.OneHour => DateTimeOffset.UtcNow.AddHours(1),
            LockDuration.OneDay => DateTimeOffset.UtcNow.AddDays(1),
            LockDuration.OneWeek => DateTimeOffset.UtcNow.AddDays(7),
            LockDuration.OneMonth => DateTimeOffset.UtcNow.AddMonths(1),
            LockDuration.Permanent => DateTimeOffset.MaxValue,
            LockDuration.Custom => DateTimeOffset.UtcNow.AddHours(customHours!.Value),
            _ => DateTimeOffset.MaxValue
        };
    }
}
