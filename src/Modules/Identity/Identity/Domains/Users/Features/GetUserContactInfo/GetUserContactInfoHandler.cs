using UserDirectory.Contracts.Queries;
using Microsoft.AspNetCore.Identity;

namespace Identity.Domains.Users.Features.GetUserContactInfo;

public sealed class GetUserContactInfoHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<GetUserContactInfoQuery, UserContactInfoDto>
{
    public async Task<UserContactInfoDto> Handle(
        GetUserContactInfoQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString());

        if (user is null)
            return new UserContactInfoDto(null, null);

        return new UserContactInfoDto(
            Email: user.Email,
            Phone: user.PhoneNumber);
    }
}
