using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Auth.Validation;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;

namespace Identity.Domains.Admins.Features.ChangeAdminPassword;

public class AdminChangePasswordCommandValidator : AbstractValidator<AdminChangePasswordCommand>
{
    public AdminChangePasswordCommandValidator()
    {
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.NewPassword).ApplyPasswordRules();
        RuleFor(x => x.ChangedByAdminId).NotEmpty();
    }
}

public class ChangeAdminPasswordHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    ISessionRevoker sessionRevoker,
    IIdentityAuditWriter audit,
    ILogger<ChangeAdminPasswordHandler> logger)
    : ICommandHandler<AdminChangePasswordCommand>
{
    public async Task<Unit> Handle(AdminChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var admin = await userManager.FindByIdAsync(command.AdminId.ToString())
                    ?? throw new AdminNotFoundException(command.AdminId);

        if (!RoleHierarchy.IsAdminOrAbove(await userManager.GetRolesAsync(admin)))
        {
            throw new AdminNotFoundException(command.AdminId);
        }

        // SuperAdmin only; never your own password through this endpoint (use POST /api/auth/change-password).
        await roleHierarchyGuard.EnsureCanManageAsync(command.ChangedByAdminId, admin, cancellationToken);

        var token = await userManager.GeneratePasswordResetTokenAsync(admin);
        var result = await userManager.ResetPasswordAsync(admin, token, command.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidUserOperationException($"Failed to change password: {errors}");
        }

        await sessionRevoker.RevokeAllAsync(admin.Id, "Password changed by SuperAdmin", cancellationToken);

        // Only the fact is audited - never the password (no before/after).
        audit.Record(IdentityAuditActions.AdminPasswordChanged, admin.Id, actorId: command.ChangedByAdminId);

        logger.LogInformation(
            "Password of admin {AdminId} changed by {ChangedBy}", command.AdminId, command.ChangedByAdminId);

        return Unit.Value;
    }
}
