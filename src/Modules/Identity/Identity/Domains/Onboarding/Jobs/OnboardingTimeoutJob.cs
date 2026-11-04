using Hangfire;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Models;

namespace Identity.Domains.Onboarding.Jobs;

/// <summary>
/// Times out an onboarding process whose OTP was never verified. Hangfire resolves this job (and the repository) in
/// its own DI scope, so the job publishes the committed domain events itself via <see cref="IDomainEventDispatcher"/>.
/// </summary>
[Queue("default")]
public class OnboardingTimeoutJob(
    IOnboardingProcessRepository repository,
    IDomainEventDispatcher domainEvents,
    ILogger<OnboardingTimeoutJob> logger)
{
    public async Task ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var process = await repository.GetActiveByUserIdAsync(userId, ct);

        if (process == null)
        {
            logger.LogInformation("No active onboarding process for user {UserId}", userId);
            return;
        }

        if (process.Status != OnboardingStatus.OtpSent)
        {
            logger.LogInformation(
                "Onboarding process for user {UserId} is in status {Status}, skipping timeout",
                userId, process.Status);
            return;
        }

        process.MarkTimedOut();

        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            // Phone verification committed first — the verify path won the race.
            // Timeout is a no-op: the process is already advancing normally.
            logger.LogInformation(
                "Timeout for user {UserId} lost concurrency race to phone verification — skipping",
                userId);
            return;
        }

        logger.LogInformation("Onboarding process for user {UserId} timed out", userId);

        // Outside mediator: publish the events of the committed save (OnboardingTimedOutEvent, ...).
        await domainEvents.DispatchPendingAsync(ct);
    }
}
