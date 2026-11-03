using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Users.Commands;
using Identity.Domains.Users.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

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
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<LockUserHandler> logger)
    : ICommandHandler<LockUserCommand, LockUserResult>
{
    public async Task<LockUserResult> Handle(LockUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureCanManageAsync(command.LockedByAdminId, user, cancellationToken);

        if (await userManager.IsLockedOutAsync(user))
        {
            throw new UserAlreadyLockedException(command.UserId);
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserAuditSnapshot.Of(user, roles);
        var lockoutEnd = CalculateLockoutEnd(command.Duration, command.CustomDurationHours);

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await sessionRevoker.RevokeAllAsync(user.Id, "Account locked by admin", cancellationToken);

        audit.Record(IdentityAuditActions.UserLocked, user.Id, reason: command.Reason,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: command.LockedByAdminId);

        await publisher.Publish(new UserLockedEvent
        {
            UserId = user.Id,
            Reason = command.Reason,
            LockoutEnd = lockoutEnd,
            LockedBy = command.LockedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} locked until {LockoutEnd} by admin {AdminId}", user.Id, lockoutEnd, command.LockedByAdminId);

        var message = lockoutEnd == DateTimeOffset.MaxValue
            ? $"User account locked permanently. Reason: {command.Reason}"
            : $"User account locked until {lockoutEnd:yyyy-MM-dd HH:mm:ss UTC}. Reason: {command.Reason}";

        return new LockUserResult(true, message, lockoutEnd);
    }

    private static DateTimeOffset CalculateLockoutEnd(LockDuration duration, int? customHours) => duration switch
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
