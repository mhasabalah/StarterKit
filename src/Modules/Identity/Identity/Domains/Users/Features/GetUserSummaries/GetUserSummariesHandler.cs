using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Users.Features.GetUserSummaries;

public sealed class GetUserSummariesHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUserSummariesQuery, IReadOnlyList<UserSummaryDto>>
{
    public async Task<IReadOnlyList<UserSummaryDto>> Handle(GetUserSummariesQuery query,
        CancellationToken cancellationToken)
    {
        if (query.UserIds.Count == 0) return [];
        if (query.UserIds.Count > GetUserSummariesQuery.MaxIds)
            throw new BadRequestException($"At most {GetUserSummariesQuery.MaxIds} user ids per request.");

        var ids = query.UserIds.Distinct().ToList();
        var rows = await dbContext.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
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
