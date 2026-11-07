namespace Notification.Dispatch;

/// <summary>
/// Turns one notification into its Hangfire jobs, all sharing ONE correlation id:
/// <list type="bullet">
/// <item><see cref="NotificationDispatchJob"/> - the email/WhatsApp fallback chain (first success wins);</item>
/// <item><see cref="InAppDeliveryJob"/> - the inbox entry + real-time event (additive);</item>
/// <item><see cref="PushDeliveryJob"/> - FCM push to the user's devices (additive).</item>
/// </list>
/// Separate jobs isolate failure domains: an FCM outage never re-runs (re-sends) the email chain and vice versa.
/// A forced additive channel (<c>SendAsync(NotificationChannel.Push)</c>) delivers ONLY that channel; a forced
/// email/WhatsApp channel narrows the chain and keeps the additive payloads.
/// </summary>
public sealed class NotificationJobEnqueuer(
    INotificationLogRepository repository,
    IBackgroundJobClient jobClient,
    ILogger<NotificationJobEnqueuer> logger) : INotificationJobEnqueuer
{
    public Task<Guid> EnqueueAsync(NotificationMessage message, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var correlationId = Guid.NewGuid();
        var forcedAdditive = forcedChannel is { } forced && forced.IsAdditive();

        var chain = message with { InApp = null, Push = null };
        // An empty message still goes to the dispatch job, which logs it (historic behaviour, visible in the log).
        var runChain = !forcedAdditive && (message.HasFallbackChainPayload || message is { InApp: null, Push: null });
        var runInApp = message.InApp is not null && (!forcedAdditive || forcedChannel == NotificationChannel.InApp);
        var runPush = message.Push is not null && (!forcedAdditive || forcedChannel == NotificationChannel.Push);

        var jobs = new List<string>(3);
        if (runChain)
        {
            jobs.Add(jobClient.Enqueue<NotificationDispatchJob>(
                job => job.ExecuteAsync(chain, correlationId, forcedChannel, CancellationToken.None)));
        }

        if (runInApp)
        {
            var inApp = new InAppDelivery(message.UserId, message.Culture, message.InApp!, message.Category);
            jobs.Add(jobClient.Enqueue<InAppDeliveryJob>(job => job.ExecuteAsync(inApp, correlationId, CancellationToken.None)));
        }

        if (runPush)
        {
            var push = new PushDelivery(message.UserId, message.Culture, message.Push!, Category: message.Category);
            jobs.Add(jobClient.Enqueue<PushDeliveryJob>(job => job.ExecuteAsync(push, correlationId, CancellationToken.None)));
        }

        logger.LogInformation(
            "Notification enqueued. TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId} HangfireJobIds: {JobIds}",
            message.TemplateName, message.UserId, correlationId, string.Join(",", jobs));

        return Task.FromResult(correlationId);
    }

    public async Task<string> ReEnqueueAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        var log = await repository.GetByIdAsync(notificationLogId, ct)
                  ?? throw new NotFoundException("NotificationLog", notificationLogId);

        if (log.Channel.IsAdditive() || forcedChannel?.IsAdditive() == true)
            throw new InvalidOperationException(
                $"Notification log '{notificationLogId}' is an {log.Channel} delivery: in-app and push deliveries retry " +
                "automatically and cannot be re-enqueued manually.");

        if (log.Status != NotificationDeliveryStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot re-enqueue notification log '{notificationLogId}': current status is '{log.Status}'. Only failed records can be re-enqueued.");

        if (string.IsNullOrWhiteSpace(log.MessageJson))
            throw new InvalidOperationException($"Notification log '{notificationLogId}' has no stored message — cannot re-enqueue.");

        var jobId = jobClient.Enqueue<NotificationDispatchJob>(
            job => job.RetryChannelAsync(notificationLogId, forcedChannel, CancellationToken.None));

        logger.LogInformation(
            "Notification re-enqueued. LogId: {LogId} NewJobId: {JobId}",
            notificationLogId, jobId);

        return jobId;
    }
}
