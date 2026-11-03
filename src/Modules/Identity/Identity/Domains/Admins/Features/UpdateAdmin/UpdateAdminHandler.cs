using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Contracts.Admins.Dtos;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;
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
    IRoleHierarchyGuard roleHierarchyGuard,
    IUserContactService contactService,
    ISessionRevoker sessionRevoker,
    IIdentityAuditWriter audit,
    ILogger<UpdateAdminHandler> logger)
    : ICommandHandler<UpdateAdminCommand, AdminDto>
{
    public async Task<AdminDto> Handle(UpdateAdminCommand command, CancellationToken cancellationToken)
    {
        var callerId = httpContextService.GetUserIdAsGuid()
                       ?? throw new UnauthorizedException("Not authenticated");

        var admin = await userManager.FindByIdAsync(command.AdminId.ToString())
                    ?? throw new AdminNotFoundException(command.AdminId);

        if (!RoleHierarchy.IsAdminOrAbove(await userManager.GetRolesAsync(admin)))
        {
            throw new AdminNotFoundException(command.AdminId);
        }

        await roleHierarchyGuard.EnsureCanManageAsync(callerId, admin, cancellationToken);

        var before = UserAuditSnapshot.Of(admin, await userManager.GetRolesAsync(admin));

        var contactChange = await contactService.ApplyAsync(admin, command.Email, command.PhoneNumber, cancellationToken);

        admin.FirstName = command.FirstName;
        admin.LastName = command.LastName;
        admin.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(admin);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidUserOperationException($"Failed to update admin: {errors}");
        }

        if (contactChange.Any)
        {
            await sessionRevoker.RevokeAllAsync(admin.Id, "Contact details changed by SuperAdmin", cancellationToken);
        }

        var roles = await userManager.GetRolesAsync(admin);

        audit.Record(IdentityAuditActions.AdminUpdated, admin.Id,
            before: before, after: UserAuditSnapshot.Of(admin, roles), actorId: callerId);

        var isLocked = await userManager.IsLockedOutAsync(admin);
        var lockoutEnd = await userManager.GetLockoutEndDateAsync(admin);

        logger.LogInformation("Admin {AdminId} updated by {CallerId}", admin.Id, callerId);

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
