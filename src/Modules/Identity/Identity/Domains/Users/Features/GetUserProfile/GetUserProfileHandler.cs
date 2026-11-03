using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Users.Features.GetUserProfile;

/// <summary><see cref="GetUserProfileQuery"/>: one user's display + contact data, <c>null</c> when unknown.</summary>
public sealed class GetUserProfileHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUserProfileQuery, UserProfileDto?>
{
    public async Task<UserProfileDto?> Handle(GetUserProfileQuery query, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == query.UserId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.PhoneNumber, u.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? null
            : new UserProfileDto(
                user.Id,
                UserDisplayName.Of(user.FirstName, user.LastName, user.Email, user.Id),
                user.Email,
                user.PhoneNumber,
                user.IsActive);
    }
}
