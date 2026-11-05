using System.Security.Cryptography;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Webhooks.Domains.Endpoints.Models;
using Webhooks.Security;

namespace Webhooks.Domains.Deliveries.Jobs;

public enum WebhookAttemptOutcome
{
    /// <summary>Nothing to do: not found, not pending, not due yet, or another worker holds the lease.</summary>
    Skipped,
    Succeeded,

    /// <summary>The attempt failed; the next one is scheduled.</summary>
    WillRetry,

    /// <summary>The attempt failed and no further attempt will be made.</summary>
    Failed,

    /// <summary>The endpoint is disabled or gone: the delivery was cancelled without sending.</summary>
    Cancelled
}

/// <summary>
/// Makes ONE delivery attempt and schedules the next. <b>Retry model</b> (single layer, Polly is not involved):
/// <list type="bullet">
/// <item>Transient failure (timeout, connection error, 408/425/429/5xx) -> record it and <c>Schedule</c> the next attempt
/// with exponential backoff, up to <see cref="WebhooksOptions.MaxAttempts"/>.</item>
/// <item>Permanent failure (blocked address, redirect, other 4xx) -> no retry; the delivery is <c>Failed</c> and can be
/// redelivered by hand.</item>
/// <item>Every failed attempt counts against the endpoint; at <see cref="WebhooksOptions.AutoDisableAfterConsecutiveFailures"/>
/// it is disabled and the tenant owner is told (domain event).</item>
/// </list>
/// The job never relies on Hangfire's own retry ([AutomaticRetry] off): the delivery row is the source of truth, and the
/// <see cref="DispatchDueWebhooksJob"/> sweep revives a delivery whose job was lost. Idempotency: the attempt is claimed
/// by leasing <c>NextAttemptAt</c> under optimistic concurrency, so duplicate jobs cannot both send; receivers
/// additionally de-duplicate on the stable <c>webhook-id</c>.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed class DeliverWebhookJob(
    IUnitOfWorkRunner unitOfWork,
    IWebhookSender sender,
    IWebhookSecretProtector secrets,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<WebhooksOptions> options,
    ILogger<DeliverWebhookJob> logger)
{
    private const int RecordRetries = 3;

    private sealed record Claim(Guid EndpointId, string Url, string SecretProtected, Guid EventId, string Payload, int Attempt);

    public async Task<WebhookAttemptOutcome> ExecuteAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        // 1. Claim (lease) the attempt.
        Claim? claim;
        try
        {
            claim = await unitOfWork.RunAsync((services, ct) => ClaimAsync(services, deliveryId, settings, ct),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            logger.LogInformation("Webhook delivery {DeliveryId} is being handled by another worker", deliveryId);
            return WebhookAttemptOutcome.Skipped;
        }

        if (claim is null)
        {
            return await ResolveUnclaimedAsync(deliveryId);
        }

        // 2. Send (no database work, no transaction held while the remote endpoint answers).
        var result = await SendAsync(claim, cancellationToken);

        // 3. Record the outcome (+ endpoint health); 4. schedule the retry.
        return await RecordAsync(deliveryId, claim, result, settings);
    }

    private async Task<WebhookSendResult> SendAsync(Claim claim, CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = secrets.Unprotect(claim.SecretProtected);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Webhook endpoint {EndpointId}: the signing secret cannot be decrypted (key ring changed?)",
                claim.EndpointId);
            return new WebhookSendResult(false, null, 0, "The signing secret cannot be read; rotate it.", null,
                WebhookFailureKind.Permanent);
        }

        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSigner.Sign(secret, claim.EventId, timestamp, claim.Payload);
        return await sender.SendAsync(new WebhookRequest(claim.Url, claim.EventId, timestamp, signature, claim.Payload),
            cancellationToken);
    }

    private static async Task<Claim?> ClaimAsync(IServiceProvider services, Guid deliveryId, WebhooksOptions settings,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<WebhooksDbContext>();
        var time = services.GetRequiredService<TimeProvider>();
        var now = time.GetUtcNow().UtcDateTime;

        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null || !delivery.IsDue(now))
            return null;

        var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.Id == delivery.EndpointId, ct);
        if (endpoint is null || !endpoint.Enabled)
        {
            delivery.Cancel("The endpoint is disabled or was deleted.");
            await db.SaveChangesAsync(ct);
            return null;
        }

        // Lease: long enough for the request + bookkeeping; the sweep ignores a lease that has not expired.
        delivery.BeginAttempt(now, TimeSpan.FromSeconds(settings.RequestTimeoutSeconds + 60));
        await db.SaveChangesAsync(ct);

        return new Claim(endpoint.Id, endpoint.Url, endpoint.SecretProtected, delivery.EventId, delivery.Payload,
            delivery.AttemptCount + 1);
    }

    /// <summary>Distinguishes "cancelled because the endpoint is gone" from "nothing to do" for the caller / tests.</summary>
    private async Task<WebhookAttemptOutcome> ResolveUnclaimedAsync(Guid deliveryId) =>
        await unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<WebhooksDbContext>();
            var status = await db.Deliveries.AsNoTracking().Where(d => d.Id == deliveryId)
                .Select(d => (WebhookDeliveryStatus?)d.Status).FirstOrDefaultAsync(ct);
            return status == WebhookDeliveryStatus.Cancelled ? WebhookAttemptOutcome.Cancelled : WebhookAttemptOutcome.Skipped;
        }, CancellationToken.None);

    private async Task<WebhookAttemptOutcome> RecordAsync(Guid deliveryId, Claim claim, WebhookSendResult result,
        WebhooksOptions settings)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (outcome, retryAt, delay) = await unitOfWork.RunAsync(
                    (services, ct) => RecordOnceAsync(services, deliveryId, claim, result, settings, ct),
                    CancellationToken.None);

                if (retryAt is not null)
                {
                    jobs.Schedule<DeliverWebhookJob>(job => job.ExecuteAsync(deliveryId, CancellationToken.None), delay);
                }

                logger.LogInformation(
                    "Webhook delivery {DeliveryId} attempt {Attempt}: {Outcome} (status {Status}, {Duration} ms)",
                    deliveryId, claim.Attempt, outcome, result.StatusCode, result.DurationMs);
                return outcome;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() && attempt < RecordRetries)
            {
                // The endpoint's health counters were updated by a parallel delivery: reload and apply again.
                logger.LogDebug(ex, "Recording webhook delivery {DeliveryId} conflicted, retrying", deliveryId);
            }
        }
    }

    private static async Task<(WebhookAttemptOutcome Outcome, DateTime? RetryAt, TimeSpan Delay)> RecordOnceAsync(
        IServiceProvider services, Guid deliveryId, Claim claim, WebhookSendResult result, WebhooksOptions settings,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<WebhooksDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var delivery = await db.Deliveries.FirstAsync(d => d.Id == deliveryId, ct);
        var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.Id == claim.EndpointId, ct);

        if (result.Success)
        {
            delivery.RecordSuccess(result.StatusCode ?? 200, result.DurationMs, result.ResponseSnippet, now);
            endpoint?.RecordSuccess(now);
            await db.SaveChangesAsync(ct);
            return (WebhookAttemptOutcome.Succeeded, null, TimeSpan.Zero);
        }

        var attemptNumber = delivery.AttemptCount + 1;
        var canRetry = result.Failure == WebhookFailureKind.Retriable && attemptNumber < settings.MaxAttempts;
        var delay = canRetry ? settings.DelayBeforeAttempt(attemptNumber + 1) : TimeSpan.Zero;
        DateTime? retryAt = canRetry ? now + delay : null;

        delivery.RecordFailure(result.StatusCode, result.DurationMs, result.Error ?? "Delivery failed.",
            result.ResponseSnippet, now, retryAt);
        endpoint?.RecordFailure(now, settings.AutoDisableAfterConsecutiveFailures, result.Error ?? "Delivery failed.");
        await db.SaveChangesAsync(ct);

        return (canRetry ? WebhookAttemptOutcome.WillRetry : WebhookAttemptOutcome.Failed, retryAt, delay);
    }
}
