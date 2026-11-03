namespace UserDirectory.Contracts.Queries;

/// <summary>
/// Active users holding the platform role <paramref name="Role"/> (e.g. who receives admin billing emails).
/// At most <paramref name="Max"/> users (1..<see cref="MaxLimit"/>), oldest first so the result is stable.
/// </summary>
public sealed record GetUsersInRoleQuery(string Role, int Max = 100) : IQuery<IReadOnlyList<UserSummaryDto>>
{
    public const int MaxLimit = 500;
}
