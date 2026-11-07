using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Identity.Domains.Sessions.Models;

/// <summary>
/// Short-lived record of "these sessions were ended". Session revocation is a set-based update on refresh tokens (no
/// aggregate is saved), so this row exists to carry <see cref="UserSessionsRevokedIntegrationEvent"/> through the
/// transactional outbox: the event is written by the SaveChanges that inserts the row, then delivered at least once.
/// Holds ids only; <c>RefreshTokenCleanupJob</c> purges old rows.
/// </summary>
public class SessionRevocation : Aggregate<Guid>
{
    public Guid UserId { get; private set; }

    public bool AllSessions { get; private set; }

    public DateTime RevokedAt { get; private set; }

    private SessionRevocation()
    {
    }

    /// <param name="sessionIds">The ended sessions; ignored when <paramref name="allSessions"/> is true.</param>
    public static SessionRevocation Create(Guid userId, IReadOnlyCollection<Guid>? sessionIds, bool allSessions, DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));
        if (!allSessions && (sessionIds is null || sessionIds.Count == 0))
            throw new ArgumentException("Session ids are required unless all sessions are revoked.", nameof(sessionIds));

        var revocation = new SessionRevocation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AllSessions = allSessions,
            RevokedAt = utcNow
        };
        revocation.AddIntegrationEvent(new UserSessionsRevokedIntegrationEvent
        {
            UserId = userId,
            AllSessions = allSessions,
            SessionIds = allSessions ? null : sessionIds!.Distinct().ToList()
        });
        return revocation;
    }
}
