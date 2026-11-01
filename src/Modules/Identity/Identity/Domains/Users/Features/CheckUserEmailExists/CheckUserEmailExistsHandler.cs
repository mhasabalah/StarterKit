using Identity.Contracts.Users.Queries;

namespace Identity.Domains.Users.Features.CheckUserEmailExists;

public sealed class CheckUserEmailExistsHandler(UserManager<ApplicationUser> userManager)
    : IQueryHandler<CheckUserEmailExistsQuery, bool>
{
    public async Task<bool> Handle(CheckUserEmailExistsQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(query.Email.Trim().ToLowerInvariant());
        return user is not null;
    }
}
