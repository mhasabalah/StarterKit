using Shared.Core.Authorization;
using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using Identity.Domains.Users.Exceptions;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.GetMyProfile;

public class GetMyProfileHandler(
    UserManager<ApplicationUser> userManager,
    IHttpContextService httpContextService)
    : IQueryHandler<GetMyProfileQuery, MyProfileDto>
{
    public async Task<MyProfileDto> Handle(
        GetMyProfileQuery query,
        CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid()
            ?? throw new ForbiddenException("User identity not found.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new UserNotFoundException(userId);

        var roles = await userManager.GetRolesAsync(user);

        return new MyProfileDto
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            ProfilePictureUrl = user.ProfilePictureUrl,
            IsProfileComplete = user.IsProfileComplete,
            Roles = roles.ToList(),
            Permissions = RolePermissionMap.For(roles),
            CreatedAt = user.CreatedAt
        };
    }
}
