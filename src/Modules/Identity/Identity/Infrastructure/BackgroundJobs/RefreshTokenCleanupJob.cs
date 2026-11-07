using Identity.Data.Repository.RefreshTokens;
using Identity.Services.Sessions;

namespace Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily Hangfire job: deletes expired refresh tokens in batches (set-based DELETE, nothing loaded into memory).
/// Revoked-but-unexpired tokens are kept on purpose: they are needed for refresh-token reuse detection.
/// </summary>
public class RefreshTokenCleanupJob(
    IRefreshTokenRepository refreshTokens,
    ISessionRevocationRecorder revocations,
    ILogger<RefreshTokenCleanupJob> logger)
{
    public const int BatchSize = 1000;
    public const int RevocationRetentionDays = 7;

    public async Task ExecuteAsync()
    {
        var deleted = await refreshTokens.DeleteExpiredAsync(DateTime.UtcNow, BatchSize);
        logger.LogInformation("Refresh token cleanup deleted {Count} expired tokens", deleted);

        // Session revocation rows only exist to carry their outbox event; a week is far beyond delivery.
        var purged = await revocations.PurgeAsync(DateTime.UtcNow.AddDays(-RevocationRetentionDays));
        logger.LogInformation("Refresh token cleanup purged {Count} old session revocation rows", purged);
    }
}
