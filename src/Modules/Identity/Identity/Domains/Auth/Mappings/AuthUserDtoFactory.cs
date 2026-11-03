using Identity.Contracts.Auth.Dtos;
using Shared.Core.Authorization;

namespace Identity.Domains.Auth.Mappings;

/// <summary>Builds the <see cref="UserDto"/> returned with every auth response (login, register, onboarding).</summary>
public static class AuthUserDtoFactory
{
    public static UserDto Create(ApplicationUser user, IEnumerable<string> roles)
    {
        var roleList = roles.ToList();

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            IsEmailVerified = user.EmailConfirmed,
            IsPhoneVerified = user.IsPhoneNumberVerified,
            IsProfileComplete = user.IsProfileComplete,
            Roles = roleList,
            Permissions = RolePermissionMap.For(roleList).ToList()
        };
    }
}
