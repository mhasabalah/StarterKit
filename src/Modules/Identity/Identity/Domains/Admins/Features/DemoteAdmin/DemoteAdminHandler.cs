using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

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
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<DemoteAdminHandler> logger)
    : ICommandHandler<DemoteAdminCommand, DemoteAdminResult>
{
    public async Task<DemoteAdminResult> Handle(DemoteAdminCommand command, CancellationToken cancellationToken)
    {
        var admin = await userManager.FindByIdAsync(command.AdminId.ToString())
                    ?? throw new AdminNotFoundException(command.AdminId);

        await roleHierarchyGuard.EnsureIsSuperAdminAsync(command.DemotedByAdminId, cancellationToken);
        await roleHierarchyGuard.EnsureCanManageAsync(command.DemotedByAdminId, admin, cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(admin);
        if (!RoleHierarchy.IsAdminOrAbove(currentRoles))
        {
            throw new AdminNotFoundException(command.AdminId);
        }

        foreach (var role in new[] { RoleConstants.Admin, RoleConstants.SuperAdmin }.Where(currentRoles.Contains))
        {
            var result = await userManager.RemoveFromRoleAsync(admin, role);
            if (!result.Succeeded)
            {
                logger.LogError("Failed to remove {Role} role from {AdminId}: {Errors}",
                    role, admin.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        admin.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(admin);

        await sessionRevoker.RevokeAllAsync(admin.Id, "Demoted from admin", cancellationToken);

        var remainingRoles = await userManager.GetRolesAsync(admin);

        audit.Record(IdentityAuditActions.AdminDemoted, admin.Id, reason: command.Reason,
            before: UserAuditSnapshot.Of(admin, currentRoles), after: UserAuditSnapshot.Of(admin, remainingRoles),
            actorId: command.DemotedByAdminId);

        await publisher.Publish(new AdminDemotedEvent
        {
            AdminId = admin.Id,
            Reason = command.Reason,
            DemotedBy = command.DemotedByAdminId
        }, cancellationToken);

        logger.LogInformation("Admin {AdminId} demoted by {DemotedBy}", admin.Id, command.DemotedByAdminId);

        return new DemoteAdminResult(
            true,
            $"Admin demoted to regular user successfully. Reason: {command.Reason}",
            remainingRoles.ToList());
    }
}
