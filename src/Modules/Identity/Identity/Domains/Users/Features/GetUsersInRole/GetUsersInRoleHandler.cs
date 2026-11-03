using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Users.Features.GetUsersInRole;

public sealed class GetUsersInRoleQueryValidator : AbstractValidator<GetUsersInRoleQuery>
{
    public GetUsersInRoleQueryValidator()
    {
        RuleFor(x => x.Role).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Max).InclusiveBetween(1, GetUsersInRoleQuery.MaxLimit);
    }
}

/// <summary><see cref="GetUsersInRoleQuery"/>: ACTIVE users holding a platform role, oldest first (stable).</summary>
public sealed class GetUsersInRoleHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUsersInRoleQuery, IReadOnlyList<UserSummaryDto>>
{
    public async Task<IReadOnlyList<UserSummaryDto>> Handle(GetUsersInRoleQuery query, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Where(u => dbContext.UserRoles.Any(ur => ur.UserId == u.Id &&
                dbContext.Roles.Any(r => r.Id == ur.RoleId && r.Name == query.Role)))
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Take(query.Max)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.ProfilePictureUrl, u.IsActive })
            .ToListAsync(cancellationToken);

        return rows
            .Select(u => new UserSummaryDto(
                u.Id,
                UserDisplayName.Of(u.FirstName, u.LastName, u.Email, u.Id),
                u.Email,
                u.ProfilePictureUrl,
                u.IsActive))
            .ToList();
    }
}
