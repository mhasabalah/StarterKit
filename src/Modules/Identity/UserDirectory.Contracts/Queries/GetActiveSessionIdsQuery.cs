namespace UserDirectory.Contracts.Queries;

/// <summary>
/// The ids (<c>sid</c> claim) of the user's sessions that are still signed in. Used to stop delivering to devices
/// bound to a session that was signed out or revoked. Empty when the user is unknown or signed out everywhere.
/// </summary>
public sealed record GetActiveSessionIdsQuery(Guid UserId) : IQuery<IReadOnlyCollection<Guid>>;
