using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Users.Features.GetUserById;

public class GetUserByIdHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<GetUserByIdHandler> logger)
    : IQueryHandler<GetUserByIdQuery, UserDto>
{
    public async Task<UserDto> Handle(
        GetUserByIdQuery query,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching user details for ID: {UserId}", query.UserId);

        var user = await userManager.FindByIdAsync(query.UserId.ToString());
        
        if (user == null)
        {
            logger.LogWarning("User not found: {UserId}", query.UserId);
            throw new UserNotFoundException(query.UserId);
        }

        // Get roles
        var roles = await userManager.GetRolesAsync(user);
        
        // Check lockout status
        var isLocked = await userManager.IsLockedOutAsync(user);
        var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);

        var userDto = new UserDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            OAuthProvider = user.OAuthProvider,
            IsActive = user.IsActive,
            IsLocked = isLocked,
            LockoutEnd = lockoutEnd,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            AccessFailedCount = user.AccessFailedCount,
            IsProfileComplete = user.IsProfileComplete,
            ProfileCompletedAt = user.ProfileCompletedAt,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt
        };

        logger.LogInformation("Successfully fetched user details for: {UserName}", user.UserName);
        
        return userDto;
    }
}
