using Identity.Data.Repository.RefreshTokens;

namespace Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Daily Hangfire job: deletes expired refresh tokens in batches (set-based DELETE, nothing loaded into memory).
/// Revoked-but-unexpired tokens are kept on purpose: they are needed for refresh-token reuse detection.
/// </summary>
public class RefreshTokenCleanupJob(
    IRefreshTokenRepository refreshTokens,
    ILogger<RefreshTokenCleanupJob> logger)
{
    public const int BatchSize = 1000;

    public async Task ExecuteAsync()
    {
        var deleted = await refreshTokens.DeleteExpiredAsync(DateTime.UtcNow, BatchSize);
        logger.LogInformation("Refresh token cleanup deleted {Count} expired tokens", deleted);
    }
}
