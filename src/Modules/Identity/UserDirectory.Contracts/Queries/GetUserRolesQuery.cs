namespace UserDirectory.Contracts.Queries;

/// <summary>
/// The platform role names of one user (e.g. <c>Admin</c>); empty when the user is unknown. Used by background work
/// that acts for a user outside an HTTP request, where there are no token claims to read the roles from.
/// </summary>
public sealed record GetUserRolesQuery(Guid UserId) : IQuery<IReadOnlyList<string>>;
