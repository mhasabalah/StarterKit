using Notification.Contracts.Push;
using UserDirectory.Contracts.Queries;

namespace Notification.Dispatch;

/// <summary>
/// Sends one push notification to the user's active devices through <see cref="IPushProvider"/>.
/// <list type="bullet">
/// <item><b>Session-bound devices.</b> A device registered under a session (<c>sid</c>) that has since been signed out
/// or revoked is deleted and skipped: a lost phone whose session an admin revoked stops receiving pushes.</item>
/// <item><b>Per-device outcomes.</b> Dead tokens are deleted; rejected messages are logged; devices that failed
/// TRANSIENTLY are retried by a follow-up job carrying only their ids, so a device that already got the message is
/// never sent it again by a retry (up to <see cref="PushOptions.MaxAttempts"/>).</item>
/// <item><b>Duplicates collapse.</b> The correlation id is the platform collapse key/tag: should a re-run (Hangfire
/// retry after an infrastructure failure) reach a device twice, the second notification replaces the first.</item>
/// <item>One <c>notification_log</c> row per attempt: Sent (at least one device accepted it), Skipped (push off / no
/// device) or Failed.</item>
/// </list>
/// </summary>
[Queue("notification")]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 600 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class PushDeliveryJob(
    ISender sender,
    IPushDeviceRepository devices,
    INotificationPreferenceRepository preferences,
    INotificationLogRepository logs,
    IPushProvider provider,
    IBackgroundJobClient jobs,
    IOptions<PushOptions> pushOptions,
    NotificationMetrics metrics,
    TimeProvider timeProvider,
    ILogger<PushDeliveryJob> logger)
{
    /// <summary>Generic text sent instead of a sensitive message (the app fetches the content over the API).</summary>
    public static readonly LocalizedText SensitiveTitle = new("New notification", "إشعار جديد");

    public static readonly LocalizedText SensitiveBody = new("Open the app to view it.", "افتح التطبيق لعرضه.");

    private const int MaxErrorLength = 2000;

    public async Task ExecuteAsync(PushDelivery delivery, Guid correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var options = pushOptions.Value;

        if (delivery.UserId == Guid.Empty)
        {
            await LogAsync(correlationId, delivery, NotificationDeliveryStatus.Skipped,
                "Push notifications need a user recipient (sent with ToEmail and no userId).", ct);
            return;
        }

        if (!await preferences.IsPushEnabledAsync(delivery.UserId, ct))
        {
            if (delivery.Attempt == 1)
                await LogAsync(correlationId, delivery, NotificationDeliveryStatus.Skipped, "The user turned push notifications off.", ct);
            return;
        }

        var targets = await ActiveDevicesAsync(delivery, ct);
        if (targets.Count == 0)
        {
            if (delivery.Attempt == 1)
                await LogAsync(correlationId, delivery, NotificationDeliveryStatus.Skipped, "No active push device.", ct);
            return;
        }

        var message = Render(delivery, correlationId, options);
        var results = await provider.SendAsync(message, targets.Select(d => new PushTarget(d.Id, d.Token, d.Platform)).ToList(), ct);

        var delivered = results.Count(r => r.Outcome == PushSendOutcome.Delivered);
        var dead = results.Where(r => r.Outcome == PushSendOutcome.InvalidToken).Select(r => r.DeviceId).ToList();
        var rejected = results.Where(r => r.Outcome == PushSendOutcome.Rejected).ToList();
        var transient = results.Where(r => r.Outcome == PushSendOutcome.Transient).Select(r => r.DeviceId).ToList();

        metrics.PushSent("delivered", delivered);
        metrics.PushSent("invalid_token", dead.Count);
        metrics.PushSent("rejected", rejected.Count);
        metrics.PushSent("transient", transient.Count);

        if (dead.Count > 0)
        {
            try
            {
                await devices.DeleteAsync(dead, CancellationToken.None);
                logger.LogInformation("Deleted {Count} push device(s) with dead FCM tokens for user {UserId}", dead.Count, delivery.UserId);
            }
            catch (Exception ex)
            {
                // Not worth a re-send: the next push reports the same tokens dead, and the stale purge removes them anyway.
                logger.LogWarning(ex, "Could not delete {Count} dead push device(s) of user {UserId}", dead.Count, delivery.UserId);
            }
        }

        foreach (var r in rejected)
            logger.LogWarning("Push to device {DeviceId} rejected: {Error}. CorrelationId: {CorrelationId}", r.DeviceId, r.Error, correlationId);

        var willRetry = transient.Count > 0 && delivery.Attempt < options.MaxAttempts;
        if (willRetry)
        {
            var next = delivery with { DeviceIds = transient, Attempt = delivery.Attempt + 1 };
            jobs.Schedule<PushDeliveryJob>(job => job.ExecuteAsync(next, correlationId, CancellationToken.None),
                options.RetryDelay(next.Attempt));
        }

        var summary = $"attempt {delivery.Attempt}: {delivered}/{results.Count} delivered, {dead.Count} dead token(s), " +
                      $"{rejected.Count} rejected, {transient.Count} transient" +
                      (willRetry ? " (retry scheduled)" : transient.Count > 0 ? " (attempts exhausted)" : string.Empty) +
                      (rejected.Count > 0 ? $"; first rejection: {rejected[0].Error}" : string.Empty);

        try
        {
            if (delivered > 0)
            {
                await LogAsync(correlationId, delivery, NotificationDeliveryStatus.Sent, delivered == results.Count ? null : summary,
                    CancellationToken.None, sent: true);
            }
            else
            {
                var kind = transient.Count > 0 ? DeliveryFailureKind.Transient : DeliveryFailureKind.Permanent;
                await LogAsync(correlationId, delivery, NotificationDeliveryStatus.Failed,
                    $"[{kind.ToString().ToLowerInvariant()}] {summary}", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // The provider already accepted the message: a Hangfire retry here would only push it again.
            logger.LogError(ex, "Push sent but its log row could not be written. CorrelationId: {CorrelationId}", correlationId);
        }

        logger.LogInformation("Push delivery {Summary}. UserId: {UserId} CorrelationId: {CorrelationId}",
            summary, delivery.UserId, correlationId);
    }

    /// <summary>
    /// The user's devices (or the follow-up subset), minus those whose session ended - which are deleted.
    /// Devices without a session id (tokens without <c>sid</c>) are kept until they go stale.
    /// </summary>
    private async Task<List<PushDevice>> ActiveDevicesAsync(PushDelivery delivery, CancellationToken ct)
    {
        var all = await devices.ListForUserAsync(delivery.UserId, delivery.DeviceIds, ct);
        if (all.Count == 0 || all.All(d => d.SessionId is null))
            return all;

        var activeSessions = (await sender.Send(new GetActiveSessionIdsQuery(delivery.UserId), ct)).ToHashSet();
        var ended = all.Where(d => d.SessionId is { } sid && !activeSessions.Contains(sid)).Select(d => d.Id).ToList();
        if (ended.Count > 0)
        {
            await devices.DeleteAsync(ended, ct);
            logger.LogInformation("Dropped {Count} push device(s) of user {UserId} whose session ended", ended.Count, delivery.UserId);
        }

        return all.Where(d => !ended.Contains(d.Id)).ToList();
    }

    internal static PushMessage Render(PushDelivery delivery, Guid correlationId, PushOptions options)
    {
        var content = delivery.Content;
        var title = content.Sensitive ? SensitiveTitle : content.Title;
        var body = content.Sensitive ? SensitiveBody : content.Body;

        var data = InAppContentRenderer.Data(content.Data, PushContent.MaxDataEntries) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        data["correlationId"] = correlationId.ToString("D");

        var ttl = content.TimeToLive is { } requested && requested > TimeSpan.Zero ? requested : options.DefaultTimeToLive;
        if (ttl > TimeSpan.FromDays(28))
            ttl = TimeSpan.FromDays(28); // FCM maximum

        return new PushMessage(
            InAppContentRenderer.Text(title.For(delivery.Culture), 200),
            InAppContentRenderer.Text(body.For(delivery.Culture), 1000),
            data,
            correlationId.ToString("N"),
            ttl);
    }

    private Task LogAsync(Guid correlationId, PushDelivery delivery, NotificationDeliveryStatus status, string? error,
        CancellationToken ct, bool sent = false)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return logs.InsertAsync(new NotificationLog
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            UserId = delivery.UserId,
            Channel = NotificationChannel.Push,
            TemplateName = delivery.Content.Data?.GetValueOrDefault("category") ?? "push",
            Status = status,
            Error = error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error,
            Culture = delivery.Culture,
            CreatedAt = now,
            SentAt = sent ? now : null
        }, ct);
    }
}
