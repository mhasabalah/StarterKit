using Identity.Contracts.Users.Dtos;

namespace Identity.Contracts.Users.Queries;

/// <summary>
/// Query to get user account status
/// </summary>
public record GetUserStatusQuery(Guid UserId) : IQuery<UserStatusDto>;
