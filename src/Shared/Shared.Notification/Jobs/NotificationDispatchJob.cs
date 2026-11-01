using Identity.Contracts.Users.Queries;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Shared.Notification.Jobs;

[Queue("notification")]
[AutomaticRetry(Attempts = 0)]
public sealed class NotificationDispatchJob(
    ISender sender,
    NotificationLogRepository repository,
    IEnumerable<INotificationChannelAdapter> adapters,
    IOptions<NotificationOptions> notificationOptions,
    NotificationDbContext db,
    ILogger<NotificationDispatchJob> logger)
{
    private static readonly ResiliencePipeline RetryPipeline =
        new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            })
            .Build();

    public async Task ExecuteAsync(
        NotificationMessage message,
        Guid correlationId,
        NotificationChannel? forcedChannel = null,
        CancellationToken ct = default)
    {
        var (email, phone) = await ResolveContactsAsync(message, ct);

        if (email is null && phone is null)
        {
            logger.LogWarning(
                "Cannot deliver notification — no contact info for user {UserId}. CorrelationId: {CorrelationId}",
                message.UserId, correlationId);
            return;
        }

        var channelOrder = forcedChannel.HasValue
            ? [forcedChannel.Value]
            : await ResolveChannelOrderAsync(message.UserId, ct);

        // Skip channels for which this message carries no payload.
        // Example: a WelcomeUser message has Email only — WhatsApp is skipped without
        // writing a Skipped log row, keeping the audit trail clean.
        var relevantChannels = channelOrder.Where(ch => message.HasPayloadFor(ch)).ToArray();
        if (relevantChannels.Length == 0)
        {
            logger.LogWarning(
                "No channel payload found for any channel in order {Order}. TemplateName: {TemplateName} UserId: {UserId}",
                string.Join(",", channelOrder), message.TemplateName, message.UserId);
            return;
        }

        var adapterMap = adapters.ToDictionary(a => a.Channel);

        foreach (var channel in relevantChannels)
        {
            if (!adapterMap.TryGetValue(channel, out var adapter))
            {
                logger.LogDebug("No adapter registered for channel {Channel} — skipping.", channel);
                continue;
            }

            if (!adapter.CanSend(email, phone))
            {
                await LogAttemptAsync(correlationId, message, channel,
                    NotificationDeliveryStatus.Skipped,
                    "Required contact detail absent (e.g., no phone for WhatsApp).",
                    null, null, ct);
                continue;
            }

            var logId = Guid.NewGuid();
            var log = new NotificationLog
            {
                Id = logId,
                CorrelationId = correlationId,
                UserId = message.UserId,
                Channel = channel,
                TemplateName = message.TemplateName,
                Status = NotificationDeliveryStatus.Queued,
                Culture = message.Culture,
                MessageJson = JsonSerializer.Serialize(message),
                CreatedAt = DateTime.UtcNow
            };
            await repository.InsertAsync(log, ct);

            try
            {
                await RetryPipeline.ExecuteAsync(async token =>
                    await adapter.SendAsync(message, email, phone, token), ct);

                await repository.UpdateStatusAsync(
                    logId, NotificationDeliveryStatus.Sent, null, DateTime.UtcNow, null, ct);

                logger.LogInformation(
                    "Notification delivered via {Channel}. TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId}",
                    channel, message.TemplateName, message.UserId, correlationId);

                return;
            }
            catch (Exception ex)
            {
                await repository.UpdateStatusAsync(
                    logId, NotificationDeliveryStatus.Failed, ex.Message, null, null, ct);

                logger.LogWarning(
                    "Channel {Channel} failed after retries. TemplateName: {TemplateName} UserId: {UserId} Error: {Error}",
                    channel, message.TemplateName, message.UserId, ex.Message);
            }
        }

        logger.LogError(
            "All channels exhausted — notification permanently failed. TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId}",
            message.TemplateName, message.UserId, correlationId);
    }

    public async Task RetryChannelAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        var log = await repository.GetByIdAsync(notificationLogId, ct)
            ?? throw new NotFoundException("NotificationLog", notificationLogId);

        if (log.Status != NotificationDeliveryStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot re-enqueue notification log '{notificationLogId}': status is '{log.Status}'. Only Failed records can be re-enqueued.");

        var message = string.IsNullOrWhiteSpace(log.MessageJson)
            ? throw new InvalidOperationException($"Notification log '{notificationLogId}' has no stored message_json — cannot re-enqueue.")
            : JsonSerializer.Deserialize<NotificationMessage>(log.MessageJson)
              ?? throw new InvalidOperationException($"Stored message_json for '{notificationLogId}' is invalid.");

        var targetChannel = forcedChannel ?? log.Channel;
        var adapterMap = adapters.ToDictionary(a => a.Channel);
        if (!adapterMap.TryGetValue(targetChannel, out var adapter))
            throw new InvalidOperationException($"No adapter registered for channel '{targetChannel}'.");

        var (email, phone) = await ResolveContactsAsync(message, ct);

        await repository.UpdateStatusAsync(notificationLogId, NotificationDeliveryStatus.Queued, null, null, null, ct);

        try
        {
            await RetryPipeline.ExecuteAsync(async token =>
                await adapter.SendAsync(message, email, phone, token), ct);

            await repository.UpdateStatusAsync(
                notificationLogId, NotificationDeliveryStatus.Sent, null, DateTime.UtcNow, null, ct);
        }
        catch (Exception ex)
        {
            await repository.UpdateStatusAsync(
                notificationLogId, NotificationDeliveryStatus.Failed, ex.Message, null, null, ct);

            logger.LogError(
                "Notification re-enqueue failed via {Channel}. LogId: {LogId} Error: {Error}",
                targetChannel, notificationLogId, ex.Message);
            throw;
        }
    }

    private async Task<NotificationChannel[]> ResolveChannelOrderAsync(Guid userId, CancellationToken ct)
    {
        var preference = await db.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (preference is not null && preference.ChannelOrder.Length > 0)
            return preference.ChannelOrder;

        return notificationOptions.Value.DefaultChannelOrder;
    }

    private async Task<(string? Email, string? Phone)> ResolveContactsAsync(
        NotificationMessage message,
        CancellationToken ct)
    {
        string? email = null;
        string? phone = null;

        if (message.UserId != Guid.Empty)
        {
            var contact = await sender.Send(new GetUserContactInfoQuery(message.UserId), ct);
            email = contact.Email;
            phone = contact.Phone;
        }

        if (!string.IsNullOrWhiteSpace(message.EmailOverride))
            email = message.EmailOverride;

        if (!string.IsNullOrWhiteSpace(message.PhoneOverride))
            phone = message.PhoneOverride;

        return (email, phone);
    }

    private async Task LogAttemptAsync(
        Guid correlationId,
        NotificationMessage message,
        NotificationChannel channel,
        NotificationDeliveryStatus status,
        string? error,
        string? hangfireJobId,
        DateTime? sentAt,
        CancellationToken ct)
    {
        var log = new NotificationLog
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            UserId = message.UserId,
            Channel = channel,
            TemplateName = message.TemplateName,
            Status = status,
            Error = error,
            Culture = message.Culture,
            HangfireJobId = hangfireJobId,
            SentAt = sentAt,
            CreatedAt = DateTime.UtcNow
        };
        await repository.InsertAsync(log, ct);
    }
}
