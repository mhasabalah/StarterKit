using Identity.Domains.Sessions.Models;
using Identity.Services.Token;
using Shared.Core.Data;
using UserDirectory.Contracts.Sessions;

namespace Identity.Services.Sessions;

/// <summary>
/// Announces ended sessions to the rest of the system (<c>UserSessionsRevokedIntegrationEvent</c>, through the
/// transactional outbox) so long-lived connections close at once.
/// <list type="bullet">
/// <item><b>Inside an open transaction</b> (the caller runs a unit of work on this context): the tombstone is only
/// added to the context; the caller's own save commits it WITH the revocation, atomically. Nothing is saved here, so no
/// other pending change is flushed by accident.</item>
/// <item><b>No open transaction</b> (the revocation itself is already committed: set-based update, Redis blacklist): the
/// tombstone is saved on a DEDICATED context through <see cref="IUnitOfWorkRunner"/>, so only the tombstone and its
/// outbox row are written. A failure there is logged, not thrown - the database revocation stays authoritative and the
/// hub still drops the socket at token expiry or on the revocation check.</item>
/// </list>
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
    IUnitOfWorkRunner unitOfWork,
    TimeProvider timeProvider,
    ILogger<SessionRevocationRecorder> logger) : ISessionRevocationRecorder
{
    public Task RecordSessionsAsync(Guid userId, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default) =>
        sessionIds.Count == 0
            ? Task.CompletedTask
            : RecordAsync(SessionRevocation.Create(userId, sessionIds, allSessions: false, Now()), ct);

    public Task RecordAllSessionsAsync(Guid userId, CancellationToken ct = default) =>
        RecordAsync(SessionRevocation.Create(userId, null, allSessions: true, Now()), ct);

    public Task<int> PurgeAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        dbContext.SessionRevocations.Where(r => r.RevokedAt < cutoffUtc).ExecuteDeleteAsync(ct);

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private async Task RecordAsync(SessionRevocation revocation, CancellationToken ct)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            // The caller's unit of work saves (and commits) it together with the revocation.
            dbContext.SessionRevocations.Add(revocation);
            return;
        }

        try
        {
            await unitOfWork.RunAsync(async (services, token) =>
            {
                var db = services.GetRequiredService<ApplicationIdentityDbContext>();
                db.SessionRevocations.Add(revocation);
                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to record the session revocation of user {UserId}; live connections close at token expiry",
                revocation.UserId);
        }
    }
}

/// <summary><see cref="ISessionRevocationChecker"/> over the access-token blacklist (Redis).</summary>
public sealed class SessionRevocationChecker(ITokenBlacklistService blacklist) : ISessionRevocationChecker
{
    public async Task<bool> IsRevokedAsync(Guid userId, Guid? sessionId, string? securityStampHash, CancellationToken ct = default) =>
        (sessionId is { } sid && await blacklist.IsSessionRevokedAsync(sid))
        || await blacklist.IsUserTokenRevokedAsync(userId, securityStampHash);
}
