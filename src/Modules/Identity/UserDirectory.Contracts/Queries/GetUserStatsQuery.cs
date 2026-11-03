namespace UserDirectory.Contracts.Queries;

public sealed record GetUserStatsQuery : IQuery<UserStatsDto>;

public sealed record UserStatsDto(int TotalUsers);
