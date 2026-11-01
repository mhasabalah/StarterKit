using Identity.Contracts.Admins.Commands;
using Identity.Contracts.Admins.Dtos;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Shared.Core.Phone;
using Shared.Core.Web;

namespace Identity.Domains.Admins.Features.UpdateAdmin;

public class UpdateAdminCommandValidator : AbstractValidator<UpdateAdminCommand>
{
    public UpdateAdminCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.PhoneNumber)
            .ValidPhoneFor("ZZ", phoneService)
            .When(x => x.PhoneNumber is not null);
    }
}

public class UpdateAdminHandler(
    UserManager<ApplicationUser> userManager,
    IHttpContextService httpContextService,
    ILogger<UpdateAdminHandler> logger)
    : ICommandHandler<UpdateAdminCommand, AdminDto>
{
    public async Task<AdminDto> Handle(
        UpdateAdminCommand command,
        CancellationToken cancellationToken)
    {
        var callerId = httpContextService.GetUserIdAsGuid();

        var admin = await userManager.FindByIdAsync(command.AdminId.ToString())
            ?? throw new AdminNotFoundException(command.AdminId);

        var targetRoles = await userManager.GetRolesAsync(admin);
        if (!targetRoles.Contains(RoleConstants.Admin) && !targetRoles.Contains(RoleConstants.SuperAdmin))
            throw new AdminNotFoundException(command.AdminId);

        // Admin cannot edit a SuperAdmin account — only SuperAdmin can
        var callerRoles = httpContextService.GetRoles().ToList();
        if (targetRoles.Contains(RoleConstants.SuperAdmin) && !callerRoles.Contains(RoleConstants.SuperAdmin))
            throw new InvalidUserOperationException("Admins cannot edit SuperAdmin accounts.");

        // Check email uniqueness if changed
        if (!string.Equals(admin.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingByEmail = await userManager.FindByEmailAsync(command.Email);
            if (existingByEmail != null && existingByEmail.Id != admin.Id)
                throw new InvalidUserOperationException($"Email '{command.Email}' is already in use.");
            await userManager.SetEmailAsync(admin, command.Email);
        }

        admin.FirstName = command.FirstName;
        admin.LastName = command.LastName;
        admin.PhoneNumber = command.PhoneNumber;
        admin.UpdatedAt = DateTime.UtcNow;

        await userManager.UpdateAsync(admin);

        var roles = await userManager.GetRolesAsync(admin);
        var isLocked = await userManager.IsLockedOutAsync(admin);
        var lockoutEnd = await userManager.GetLockoutEndDateAsync(admin);

        logger.LogInformation("Admin {AdminId} updated by {CallerId}", command.AdminId, callerId);

        return new AdminDto
        {
            Id = admin.Id,
            UserName = admin.UserName!,
            Email = admin.Email!,
            PhoneNumber = admin.PhoneNumber,
            FirstName = admin.FirstName,
            LastName = admin.LastName,
            FullName = admin.FullName,
            ProfilePictureUrl = admin.ProfilePictureUrl,
            Roles = roles.ToList(),
            IsSuperAdmin = roles.Contains(RoleConstants.SuperAdmin),
            IsActive = admin.IsActive,
            IsLocked = isLocked,
            LockoutEnd = lockoutEnd,
            CreatedAt = admin.CreatedAt,
            UpdatedAt = admin.UpdatedAt,
            LastLoginAt = admin.LastLoginAt
        };
    }
}
