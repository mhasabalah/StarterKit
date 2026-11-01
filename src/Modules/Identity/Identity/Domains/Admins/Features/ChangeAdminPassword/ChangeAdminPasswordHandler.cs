using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Admins.Features.ChangeAdminPassword;

public class AdminChangePasswordCommandValidator : AbstractValidator<AdminChangePasswordCommand>
{
    public AdminChangePasswordCommandValidator()
    {
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.ChangedByAdminId).NotEmpty();
    }
}

public class ChangeAdminPasswordHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<ChangeAdminPasswordHandler> logger)
    : ICommandHandler<AdminChangePasswordCommand>
{
    public async Task<Unit> Handle(
        AdminChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var admin = await userManager.FindByIdAsync(command.AdminId.ToString())
            ?? throw new AdminNotFoundException(command.AdminId);

        var targetRoles = await userManager.GetRolesAsync(admin);
        if (!targetRoles.Contains(RoleConstants.Admin) && !targetRoles.Contains(RoleConstants.SuperAdmin))
            throw new AdminNotFoundException(command.AdminId);

        // Admin cannot change a SuperAdmin's password — only SuperAdmin can
        var caller = await userManager.FindByIdAsync(command.ChangedByAdminId.ToString())
            ?? throw new UserNotFoundException(command.ChangedByAdminId);
        var callerRoles = await userManager.GetRolesAsync(caller);
        if (targetRoles.Contains(RoleConstants.SuperAdmin) && !callerRoles.Contains(RoleConstants.SuperAdmin))
            throw new InvalidUserOperationException("Admins cannot change a SuperAdmin's password.");

        // Use password reset token — no old password required for admin override
        var token = await userManager.GeneratePasswordResetTokenAsync(admin);
        var result = await userManager.ResetPasswordAsync(admin, token, command.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidUserOperationException($"Failed to change password: {errors}");
        }

        logger.LogInformation(
            "Password changed for admin {AdminId} by {ChangedBy}",
            command.AdminId, command.ChangedByAdminId);

        return Unit.Value;
    }
}
