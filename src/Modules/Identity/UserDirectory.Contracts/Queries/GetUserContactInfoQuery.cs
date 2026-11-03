namespace UserDirectory.Contracts.Queries;

/// <summary>How to reach a user. Both fields are <c>null</c> when the user does not exist.</summary>
public sealed record GetUserContactInfoQuery(Guid UserId) : IQuery<UserContactInfoDto>;

public sealed record UserContactInfoDto(string? Email, string? Phone);
