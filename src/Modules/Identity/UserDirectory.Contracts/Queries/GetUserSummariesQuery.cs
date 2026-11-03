namespace UserDirectory.Contracts.Queries;

/// <summary>
/// Batch display data of users (e.g. a tenant's member list). Unknown ids are omitted. At most
/// <see cref="MaxIds"/> ids per call.
/// </summary>
public sealed record GetUserSummariesQuery(IReadOnlyCollection<Guid> UserIds) : IQuery<IReadOnlyList<UserSummaryDto>>
{
    public const int MaxIds = 500;
}

/// <param name="DisplayName">"First Last" when set, otherwise the email, otherwise the id - never empty.</param>
public sealed record UserSummaryDto(Guid UserId, string DisplayName, string? Email, string? ProfilePictureUrl, bool IsActive);
