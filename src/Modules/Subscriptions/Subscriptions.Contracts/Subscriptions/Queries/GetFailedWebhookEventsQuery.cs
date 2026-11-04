using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

/// <summary>Inbound payment webhooks that failed processing (newest first). Money may have been received.</summary>
public record GetFailedWebhookEventsQuery : BaseQuery, IQuery<PaginatedResult<InboundWebhookEventDto>>
{
    public string? Provider { get; init; }
}
