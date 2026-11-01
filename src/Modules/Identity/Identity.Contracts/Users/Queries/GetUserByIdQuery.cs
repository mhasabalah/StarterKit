using Identity.Contracts.Users.Dtos;

namespace Identity.Contracts.Users.Queries;

/// <summary>
/// Query to get detailed user information by ID
/// </summary>
public record GetUserByIdQuery(Guid UserId) : IQuery<UserDto>;
