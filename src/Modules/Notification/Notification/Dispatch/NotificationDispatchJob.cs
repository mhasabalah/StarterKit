using UserDirectory.Contracts.Queries;

namespace Notification.Dispatch;

/// <summary>
/// Delivers one notification over the user's channel order with fallback.
/// <para><b>Retry model</b> (single layer - Hangfire; the WhatsApp HttpClient has no retry of its own):</para>
/// <list type="bullet">
/// <item><b>Permanent</b> failure (4xx, bad template, invalid recipient) → no retry, fall back to the next channel.</item>
/// <item><b>Transient</b> failure (5xx, 429, connection refused) → throw so Hangfire retries the job; after
/// <see cref="NotificationOptions.FallbackAfterAttempts"/> failed attempts on that channel, fall back.</item>
/// <item><b>Ambiguous</b> failure (timeout) → retry the same channel only; never fall back, because the
/// message may already have been delivered (fallback would send it twice).</item>
/// </list>
/// <para><b>Idempotency</b>: every attempt writes a <c>notification_log</c> row keyed by the correlation id.
/// A re-run that finds a Sent row stops immediately, so retries never re-send a delivered notification.</para>
/// </summary>
[Queue("notification")]
[AutomaticRetry(Attempts = 4, DelaysInSeconds = new[] { 30, 120, 600, 1800 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class NotificationDispatchJob(
    ISender sender,
    INotificationLogRepository repository,
    INotificationPreferenceRepository preferences,
    IEnumerable<INotificationChannelAdapter> adapters,
    IOptions<NotificationOptions> notificationOptions,
    ILogger<NotificationDispatchJob> logger)
{
    private const int MaxErrorLength = 2000;

    public async Task ExecuteAsync(
        NotificationMessage message,
        Guid correlationId,
        NotificationChannel? forcedChannel = null,
        CancellationToken ct = default)
    {
        var history = await repository.GetByCorrelationAsync(correlationId, ct);
        if (history.Any(h => h.Status == NotificationDeliveryStatus.Sent))
        {
            logger.LogInformation(
                "Notification already delivered - skipping re-run. CorrelationId: {CorrelationId}", correlationId);
            return;
        }

        var (email, phone) = await ResolveContactsAsync(message, ct);

        if (email is null && phone is null)
        {
            logger.LogWarning(
                "Cannot deliver notification - no contact info for user {UserId}. CorrelationId: {CorrelationId}",
                message.UserId, correlationId);
            return;
        }

        var channelOrder = forcedChannel.HasValue
            ? [forcedChannel.Value]
            : await preferences.ResolveChannelOrderAsync(message.UserId, ct);

        // Skip channels for which this message carries no payload (no Skipped row - keeps the audit trail clean).
        var relevantChannels = channelOrder.Where(message.HasPayloadFor).ToArray();
        if (relevantChannels.Length == 0)
        {
            logger.LogWarning(
                "No channel payload found for any channel in order {Order}. TemplateName: {TemplateName} UserId: {UserId}",
                string.Join(",", channelOrder), message.TemplateName, message.UserId);
            return;
        }

        var opts = notificationOptions.Value;
        var messageJson = NotificationMessageSerializer.SerializeForLog(message);

        foreach (var channel in relevantChannels)
        {
            var adapter = adapters.FirstOrDefault(a => a.Channel == channel);
            if (adapter is null)
            {
                logger.LogDebug("No adapter registered for channel {Channel} - skipping.", channel);
                continue;
            }

            var channelHistory = history.Where(h => h.Channel == channel).ToList();
            if (IsChannelExhausted(channelHistory, opts.FallbackAfterAttempts))
                continue;

            if (!adapter.CanSend(email, phone))
            {
                if (channelHistory.All(h => h.Status != NotificationDeliveryStatus.Skipped))
                {
                    await repository.InsertAsync(NewLog(correlationId, message, channel,
                        NotificationDeliveryStatus.Skipped, null,
                        "Required contact detail absent (e.g., no phone for WhatsApp)."), ct);
                }
                continue;
            }

            var log = NewLog(correlationId, message, channel, NotificationDeliveryStatus.Queued, messageJson, null);
            await repository.InsertAsync(log, ct);

            try
            {
                await adapter.SendAsync(message, email, phone, ct);
            }
            catch (Exception ex)
            {
                var kind = DeliveryFailureClassifier.Classify(ex);
                await repository.UpdateStatusAsync(
                    log.Id, NotificationDeliveryStatus.Failed, FormatError(kind, ex), null, null, CancellationToken.None);

                var transientAttempts = CountFailures(channelHistory, DeliveryFailureKind.Transient)
                                        + (kind == DeliveryFailureKind.Transient ? 1 : 0);
                var pinned = kind == DeliveryFailureKind.Ambiguous
                             || CountFailures(channelHistory, DeliveryFailureKind.Ambiguous) > 0;

                logger.LogWarning(
                    "Channel {Channel} failed ({Kind}). TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId} Error: {Error}",
                    channel, kind, message.TemplateName, message.UserId, correlationId, ex.Message);

                if (kind == DeliveryFailureKind.Permanent)
                    continue; // never retried - try the next channel

                if (!pinned && transientAttempts >= opts.FallbackAfterAttempts)
                    continue; // definitely not delivered, attempts used up - fall back

                // Retry the SAME channel via Hangfire (ambiguous outcomes must not fall back).
                throw new NotificationDeliveryException(channel, kind, ex);
            }

            // Delivered. A status-update failure here must not cause a retry (which would re-send).
            try
            {
                await repository.UpdateStatusAsync(
                    log.Id, NotificationDeliveryStatus.Sent, null, DateTime.UtcNow, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Notification delivered via {Channel} but its log row could not be marked Sent. LogId: {LogId}",
                    channel, log.Id);
            }

            logger.LogInformation(
                "Notification delivered via {Channel}. TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId}",
                channel, message.TemplateName, message.UserId, correlationId);
            return;
        }

        logger.LogError(
            "All channels exhausted - notification permanently failed. TemplateName: {TemplateName} UserId: {UserId} CorrelationId: {CorrelationId}",
            message.TemplateName, message.UserId, correlationId);
    }

    /// <summary>Admin re-enqueue of one failed log row on its channel (or a forced one).</summary>
    public async Task RetryChannelAsync(Guid notificationLogId, NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        var log = await repository.GetByIdAsync(notificationLogId, ct)
            ?? throw new NotFoundException("NotificationLog", notificationLogId);

        if (log.Status == NotificationDeliveryStatus.Sent)
        {
            logger.LogInformation("Notification log {LogId} already Sent - skipping re-run.", notificationLogId);
            return;
        }

        if (log.Status != NotificationDeliveryStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot re-enqueue notification log '{notificationLogId}': status is '{log.Status}'. Only Failed records can be re-enqueued.");

        var message = string.IsNullOrWhiteSpace(log.MessageJson)
            ? throw new InvalidOperationException($"Notification log '{notificationLogId}' has no stored message_json - cannot re-enqueue.")
            : NotificationMessageSerializer.DeserializeFromLog(log.MessageJson)
              ?? throw new InvalidOperationException($"Stored message_json for '{notificationLogId}' is invalid.");

        var targetChannel = forcedChannel ?? log.Channel;
        var adapter = adapters.FirstOrDefault(a => a.Channel == targetChannel)
                      ?? throw new InvalidOperationException($"No adapter registered for channel '{targetChannel}'.");

        var (email, phone) = await ResolveContactsAsync(message, ct);

        await repository.UpdateStatusAsync(notificationLogId, NotificationDeliveryStatus.Queued, null, null, null, ct);

        try
        {
            await adapter.SendAsync(message, email, phone, ct);
        }
        catch (Exception ex)
        {
            var kind = DeliveryFailureClassifier.Classify(ex);
            await repository.UpdateStatusAsync(
                notificationLogId, NotificationDeliveryStatus.Failed, FormatError(kind, ex), null, null, CancellationToken.None);

            logger.LogError(
                "Notification re-enqueue failed via {Channel} ({Kind}). LogId: {LogId} Error: {Error}",
                targetChannel, kind, notificationLogId, ex.Message);

            if (kind == DeliveryFailureKind.Permanent)
                return; // retrying cannot help

            throw new NotificationDeliveryException(targetChannel, kind, ex);
        }

        await repository.UpdateStatusAsync(
            notificationLogId, NotificationDeliveryStatus.Sent, null, DateTime.UtcNow, null, CancellationToken.None);
    }

    // ----- helpers -----

    /// <summary>
    /// A channel is done for this correlation when it was skipped, failed permanently, or used up its
    /// transient attempts - unless it ever had an ambiguous (timeout) failure, which pins it.
    /// </summary>
    private static bool IsChannelExhausted(IReadOnlyCollection<NotificationLog> channelHistory, int fallbackAfterAttempts)
    {
        if (channelHistory.Any(h => h.Status == NotificationDeliveryStatus.Skipped))
            return true;

        if (CountFailures(channelHistory, DeliveryFailureKind.Permanent) > 0)
            return true;

        if (CountFailures(channelHistory, DeliveryFailureKind.Ambiguous) > 0)
            return false;

        return CountFailures(channelHistory, DeliveryFailureKind.Transient) >= fallbackAfterAttempts;
    }

    private static int CountFailures(IEnumerable<NotificationLog> rows, DeliveryFailureKind kind)
        => rows.Count(h => h.Status == NotificationDeliveryStatus.Failed && ParseKind(h.Error) == kind);

    /// <summary>Error column format: "[permanent] message", "[transient] ...", "[ambiguous] ...".</summary>
    internal static string FormatError(DeliveryFailureKind kind, Exception ex)
    {
        var text = $"[{kind.ToString().ToLowerInvariant()}] {ex.GetType().Name}: {ex.Message}";
        return text.Length > MaxErrorLength ? text[..MaxErrorLength] : text;
    }

    internal static DeliveryFailureKind ParseKind(string? error)
    {
        if (error is null)
            return DeliveryFailureKind.Transient;
        if (error.StartsWith("[permanent]", StringComparison.Ordinal))
            return DeliveryFailureKind.Permanent;
        if (error.StartsWith("[ambiguous]", StringComparison.Ordinal))
            return DeliveryFailureKind.Ambiguous;
        return DeliveryFailureKind.Transient;
    }

    private static NotificationLog NewLog(
        Guid correlationId,
        NotificationMessage message,
        NotificationChannel channel,
        NotificationDeliveryStatus status,
        string? messageJson,
        string? error) => new()
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            UserId = message.UserId,
            Channel = channel,
            TemplateName = message.TemplateName,
            Status = status,
            Error = error,
            Culture = message.Culture,
            MessageJson = messageJson,
            CreatedAt = DateTime.UtcNow
        };

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
}

/// <summary>Thrown to make Hangfire retry a dispatch after a transient or ambiguous channel failure.</summary>
public sealed class NotificationDeliveryException(NotificationChannel channel, DeliveryFailureKind kind, Exception inner)
    : Exception($"Delivery via {channel} failed ({kind}): {inner.Message}", inner)
{
    public NotificationChannel Channel { get; } = channel;
    public DeliveryFailureKind Kind { get; } = kind;
}
