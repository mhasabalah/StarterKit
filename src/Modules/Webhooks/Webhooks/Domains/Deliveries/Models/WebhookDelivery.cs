namespace Webhooks.Domains.Deliveries.Models;

/// <summary>
/// One message to one endpoint, with all its attempts. <see cref="EventId"/> is the stable <c>webhook-id</c> receivers
/// de-duplicate on: it is identical across retries and manual redeliveries.
/// <para>
/// <see cref="NextAttemptAt"/> doubles as the lease: <see cref="BeginAttempt"/> pushes it into the future (saved with the
/// optimistic version, so two workers can never both claim the same attempt), and the sweep job re-enqueues
/// deliveries that stay overdue.
/// </para>
/// </summary>
public class WebhookDelivery : Entity<Guid>, IMustHaveTenant
{
    public const int EventTypeMaxLength = 200;
    public const int ErrorMaxLength = 1000;
    public const int SnippetMaxLength = 1000;

    public Guid TenantId { get; set; }

    public Guid EndpointId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = default!;

    /// <summary>The exact JSON body that is signed and sent.</summary>
    public string Payload { get; private set; } = default!;

    public WebhookDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int? LastResponseCode { get; private set; }
    public int? LastDurationMs { get; private set; }
    public string? LastError { get; private set; }
    public string? LastResponseSnippet { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? LastAttemptAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }

    /// <summary>Set on manual redeliveries: the delivery this one repeats.</summary>
    public Guid? RedeliveryOfId { get; private set; }

    private WebhookDelivery()
    {
    }

    public static WebhookDelivery Create(Guid endpointId, Guid eventId, string eventType, string payload, DateTime now,
        Guid? redeliveryOfId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EndpointId = endpointId,
            EventId = eventId,
            EventType = eventType,
            Payload = payload,
            Status = WebhookDeliveryStatus.Pending,
            NextAttemptAt = now,
            RedeliveryOfId = redeliveryOfId
        };

    public WebhookDelivery Redeliver(DateTime now) => Create(EndpointId, EventId, EventType, Payload, now, Id);

    /// <summary>True when an attempt may start now (pending and due, with a little clock tolerance).</summary>
    public bool IsDue(DateTime now) =>
        Status == WebhookDeliveryStatus.Pending && (NextAttemptAt is null || NextAttemptAt <= now.AddSeconds(5));

    /// <summary>Claims the attempt: leases the delivery until <paramref name="now"/> + <paramref name="lease"/>.</summary>
    public void BeginAttempt(DateTime now, TimeSpan lease) => NextAttemptAt = now + lease;

    /// <summary>The sweep found it overdue and queued a job: look again no earlier than the stuck threshold from now.</summary>
    public void MarkEnqueued(DateTime now) => NextAttemptAt = now;

    public void RecordSuccess(int responseCode, int durationMs, string? snippet, DateTime now)
    {
        AttemptCount++;
        Status = WebhookDeliveryStatus.Succeeded;
        LastResponseCode = responseCode;
        LastDurationMs = durationMs;
        LastError = null;
        LastResponseSnippet = Clip(snippet, SnippetMaxLength);
        LastAttemptAt = now;
        DeliveredAt = now;
        NextAttemptAt = null;
    }

    /// <summary>Records a failed attempt and either schedules the next one (<paramref name="retryAt"/>) or gives up.</summary>
    public void RecordFailure(int? responseCode, int durationMs, string error, string? snippet, DateTime now,
        DateTime? retryAt)
    {
        AttemptCount++;
        LastResponseCode = responseCode;
        LastDurationMs = durationMs;
        LastError = Clip(error, ErrorMaxLength);
        LastResponseSnippet = Clip(snippet, SnippetMaxLength);
        LastAttemptAt = now;
        Status = retryAt is null ? WebhookDeliveryStatus.Failed : WebhookDeliveryStatus.Pending;
        NextAttemptAt = retryAt;
    }

    public void Cancel(string reason)
    {
        if (Status != WebhookDeliveryStatus.Pending)
            return;
        Status = WebhookDeliveryStatus.Cancelled;
        LastError = Clip(reason, ErrorMaxLength);
        NextAttemptAt = null;
    }

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
