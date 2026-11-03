using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

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
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<PromoteToAdminHandler> logger)
    : ICommandHandler<PromoteToAdminCommand, PromoteToAdminResult>
{
    public async Task<PromoteToAdminResult> Handle(PromoteToAdminCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureIsSuperAdminAsync(command.PromotedByAdminId, cancellationToken);
        await roleHierarchyGuard.EnsureCanManageAsync(command.PromotedByAdminId, user, cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(user);

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

        var rolesToAdd = new List<string> { RoleConstants.User, RoleConstants.Admin };
        if (command.MakeSuperAdmin)
        {
            rolesToAdd.Add(RoleConstants.SuperAdmin);
        }

        foreach (var role in rolesToAdd.Where(r => !currentRoles.Contains(r)))
        {
            var result = await userManager.AddToRoleAsync(user, role);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return new PromoteToAdminResult(false, $"Failed to promote to {role}: {errors}", currentRoles.ToList());
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Live tokens carry the old role set: end every session (rotates the security stamp) so the promotion takes
        // effect now and the admin session starts from a fresh login (and its 2FA step).
        await sessionRevoker.RevokeAllAsync(user.Id, "Promoted to admin", cancellationToken);

        var newRoles = await userManager.GetRolesAsync(user);

        audit.Record(IdentityAuditActions.AdminPromoted, user.Id, reason: command.Reason,
            before: UserAuditSnapshot.Of(user, currentRoles), after: UserAuditSnapshot.Of(user, newRoles),
            actorId: command.PromotedByAdminId);

        await publisher.Publish(new UserPromotedToAdminEvent
        {
            UserId = user.Id,
            IsSuperAdmin = command.MakeSuperAdmin,
            Reason = command.Reason,
            PromotedBy = command.PromotedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "User {UserId} promoted to {AdminType} by {PromotedBy}",
            user.Id, command.MakeSuperAdmin ? RoleConstants.SuperAdmin : RoleConstants.Admin, command.PromotedByAdminId);

        var message = command.MakeSuperAdmin
            ? $"User promoted to SuperAdmin successfully. Reason: {command.Reason}"
            : $"User promoted to Admin successfully. Reason: {command.Reason}";

        return new PromoteToAdminResult(true, message, newRoles.ToList());
    }
}
