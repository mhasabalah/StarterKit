using Identity.Data;

namespace Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Background job to clean up expired refresh tokens from database
/// Runs daily via Hangfire
/// </summary>
public class RefreshTokenCleanupJob(
    ApplicationIdentityDbContext dbContext,
    ILogger<RefreshTokenCleanupJob> logger)
{
    /// <summary>
    /// Remove expired refresh tokens from database
    /// </summary>
    public async Task ExecuteAsync()
    {
        logger.LogInformation("Starting refresh token cleanup job");

        try
        {
            var cutoffDate = DateTime.UtcNow;

            // Find all expired tokens
            var expiredTokens = await dbContext.RefreshTokens
                .Where(rt => rt.ExpiresAt < cutoffDate)
                .ToListAsync();

            if (expiredTokens.Any())
            {
                dbContext.RefreshTokens.RemoveRange(expiredTokens);
                await dbContext.SaveChangesAsync();

                logger.LogInformation(
                    "Cleaned up {Count} expired refresh tokens",
                    expiredTokens.Count);
            }
            else
            {
                logger.LogInformation("No expired refresh tokens to clean up");
            }

            logger.LogInformation("Refresh token cleanup job completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during refresh token cleanup");
            throw;
        }
    }
}
