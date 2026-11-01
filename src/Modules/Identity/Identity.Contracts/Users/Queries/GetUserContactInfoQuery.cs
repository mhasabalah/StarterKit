namespace Identity.Contracts.Users.Queries;

public sealed record GetUserContactInfoQuery(Guid UserId) : IQuery<UserContactInfoDto>;

public sealed record UserContactInfoDto(string? Email, string? Phone);
