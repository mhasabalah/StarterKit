using Identity.Domains.Sessions.Models;
using Identity.Services.Token;
using Shared.Core.Authorization;

namespace Identity.Services.Sessions;

/// <summary>
/// Announces ended sessions to the rest of the system (<c>UserSessionsRevokedIntegrationEvent</c>, through the
/// transactional outbox) so long-lived connections close at once. Called AFTER the revocation itself committed
/// (refresh tokens revoked, sid blacklisted): the database stays authoritative, and a failure to record is logged, not
/// thrown - the hub still drops the socket at the next access-token expiry and its per-invocation revocation check.
/// </summary>
public interface ISessionRevocationRecorder
{
    Task RecordSessionsAsync(Guid userId, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default);

    Task RecordAllSessionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Deletes revocation rows older than <paramref name="cutoffUtc"/> (their outbox events were long delivered).</summary>
    Task<int> PurgeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}

public sealed class SessionRevocationRecorder(
    ApplicationIdentityDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<SessionRevocationRecorder> logger) : ISessionRevocationRecorder
{
    public Task RecordSessionsAsync(Guid userId, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default) =>
        sessionIds.Count == 0
            ? Task.CompletedTask
            : SaveAsync(SessionRevocation.Create(userId, sessionIds, allSessions: false, Now()), ct);

    public Task RecordAllSessionsAsync(Guid userId, CancellationToken ct = default) =>
        SaveAsync(SessionRevocation.Create(userId, null, allSessions: true, Now()), ct);

    public Task<int> PurgeAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        dbContext.SessionRevocations.Where(r => r.RevokedAt < cutoffUtc).ExecuteDeleteAsync(ct);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private async Task SaveAsync(SessionRevocation revocation, CancellationToken ct)
    {
        dbContext.SessionRevocations.Add(revocation);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            dbContext.Entry(revocation).State = EntityState.Detached;
            logger.LogError(ex, "Failed to record the session revocation of user {UserId}; live connections close at token expiry",
                revocation.UserId);
        }
    }
}

/// <summary><see cref="ISessionRevocationChecker"/> over the access-token blacklist (Redis).</summary>
public sealed class SessionRevocationChecker(ITokenBlacklistService blacklist) : ISessionRevocationChecker
{
    public Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken ct = default) => blacklist.IsSessionRevokedAsync(sessionId);
}
