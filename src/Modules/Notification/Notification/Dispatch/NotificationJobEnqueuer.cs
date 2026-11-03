namespace Notification.Dispatch;

public sealed class NotificationJobEnqueuer(
    INotificationLogRepository repository,
    IBackgroundJobClient jobClient,
    ILogger<NotificationJobEnqueuer> logger) : INotificationJobEnqueuer
{
    public Task<Guid> EnqueueAsync(NotificationMessage message, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();

        var jobId = jobClient.Enqueue<NotificationDispatchJob>(
            job => job.ExecuteAsync(message, correlationId, forcedChannel, CancellationToken.None));

        logger.LogInformation(
            "Notification enqueued. TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId} HangfireJobId: {JobId}",
            message.TemplateName, message.UserId, correlationId, jobId);

        return Task.FromResult(correlationId);
    }

    public async Task<string> ReEnqueueAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        var log = await repository.GetByIdAsync(notificationLogId, ct)
                  ?? throw new NotFoundException("NotificationLog", notificationLogId);

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
