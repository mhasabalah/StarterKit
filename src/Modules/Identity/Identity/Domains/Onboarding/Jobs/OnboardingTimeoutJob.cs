using Hangfire;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Models;

namespace Identity.Domains.Onboarding.Jobs;

[Queue("default")]
public class OnboardingTimeoutJob(
    IOnboardingProcessRepository repository,
    IDomainEventCollector collector,
    IPublisher publisher,
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
        catch (DbUpdateConcurrencyException)
        {
            // Phone verification committed first — the verify path won the race.
            // Timeout is a no-op: the process is already advancing normally.
            logger.LogInformation(
                "Timeout for user {UserId} lost concurrency race to phone verification — skipping",
                userId);
            return;
        }

        logger.LogInformation("Onboarding process for user {UserId} timed out", userId);

        // Hangfire jobs run outside the MediatR pipeline, so DispatchDomainEventsBehavior
        // never fires here. The EF interceptor already collected events into IDomainEventCollector
        // during SaveChangesAsync above — dispatch them manually now.
        var events = collector.DequeueAll();
        foreach (var domainEvent in events)
            await publisher.Publish(domainEvent, ct);
    }
}
