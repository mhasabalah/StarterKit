using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Users.Features.GetUserStats;

public class GetUserStatsHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUserStatsQuery, UserStatsDto>
{
    public async Task<UserStatsDto> Handle(GetUserStatsQuery query, CancellationToken cancellationToken)
    {
        var total = await dbContext.Users.CountAsync(cancellationToken);
        return new UserStatsDto(total);
    }
}
