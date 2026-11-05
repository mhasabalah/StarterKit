using Identity.Contracts.Admins.Dtos;
using Identity.Contracts.Admins.Queries;
using Identity.Domains.Admins.Exceptions;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Admins.Features.GetAdminById;

public class GetAdminByIdHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<GetAdminByIdHandler> logger)
    : IQueryHandler<GetAdminByIdQuery, AdminDto>
{
    public async Task<AdminDto> Handle(
        GetAdminByIdQuery query,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching admin details for ID: {AdminId}", query.AdminId);

        var admin = await userManager.FindByIdAsync(query.AdminId.ToString());

        if (admin == null)
        {
            logger.LogWarning("Admin not found: {AdminId}", query.AdminId);
            throw new AdminNotFoundException(query.AdminId);
        }

        // Verify user is actually an admin
        var roles = await userManager.GetRolesAsync(admin);
        if (!roles.Contains(RoleConstants.Admin) && !roles.Contains(RoleConstants.SuperAdmin))
        {
            logger.LogWarning("User {UserId} is not an admin", query.AdminId);
            throw new AdminNotFoundException(query.AdminId);
        }

        var isSuperAdmin = roles.Contains(RoleConstants.SuperAdmin);
        var isLocked = await userManager.IsLockedOutAsync(admin);
        var lockoutEnd = await userManager.GetLockoutEndDateAsync(admin);

        var adminDto = new AdminDto
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
            IsSuperAdmin = isSuperAdmin,
            LastAdminActionAt = admin.UpdatedAt,
            IsActive = admin.IsActive,
            IsLocked = isLocked,
            LockoutEnd = lockoutEnd,
            CreatedAt = admin.CreatedAt,
            UpdatedAt = admin.UpdatedAt,
            LastLoginAt = admin.LastLoginAt
        };

        logger.LogInformation("Successfully fetched admin details for: {UserName}", admin.UserName);

        return adminDto;
    }
}
