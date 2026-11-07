namespace UserDirectory.Contracts.Queries;

/// <summary>
/// Resolves e-mail addresses to user ids (bulk member import: "is this address already a user?"). Matching is
/// case-insensitive; addresses without a user are omitted; the returned map is case-insensitive too.
/// At most <see cref="MaxEmails"/> addresses per call.
/// </summary>
public sealed record GetUserIdsByEmailsQuery(IReadOnlyCollection<string> Emails)
    : IQuery<IReadOnlyDictionary<string, Guid>>
{
    public const int MaxEmails = 500;
}
