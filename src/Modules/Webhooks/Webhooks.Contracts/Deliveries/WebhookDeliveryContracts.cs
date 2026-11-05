namespace Webhooks.Contracts.Deliveries;

public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid EndpointId,
    Guid EventId,
    string EventType,
    WebhookDeliveryStatus Status,
    int AttemptCount,
    int? LastResponseCode,
    int? LastDurationMs,
    string? LastError,
    DateTime? NextAttemptAt,
    DateTime? LastAttemptAt,
    DateTime? DeliveredAt,
    Guid? RedeliveryOfId,
    DateTime CreatedAt);

/// <summary>Detail view: adds the exact signed payload and the (truncated) last response body.</summary>
public sealed record WebhookDeliveryDetailDto(
    Guid Id,
    Guid EndpointId,
    Guid EventId,
    string EventType,
    WebhookDeliveryStatus Status,
    int AttemptCount,
    int? LastResponseCode,
    int? LastDurationMs,
    string? LastError,
    string? LastResponseSnippet,
    DateTime? NextAttemptAt,
    DateTime? LastAttemptAt,
    DateTime? DeliveredAt,
    Guid? RedeliveryOfId,
    DateTime CreatedAt,
    string Payload);

public sealed record GetWebhookDeliveriesQuery : BaseQuery, IQuery<PaginatedResult<WebhookDeliveryDto>>
{
    public Guid? EndpointId { get; init; }
    public WebhookDeliveryStatus? Status { get; init; }
    public string? EventType { get; init; }
}

public sealed record GetWebhookDeliveryByIdQuery(Guid Id) : IQuery<WebhookDeliveryDetailDto>;

/// <summary>Queues a fresh delivery of the same event (same webhook-id) to the same endpoint.</summary>
public sealed record RedeliverWebhookCommand(Guid DeliveryId) : ICommand<RedeliverWebhookResult>;

public sealed record RedeliverWebhookResult(Guid DeliveryId);
