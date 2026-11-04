namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <param name="Attempts">Processing attempts so far; the retry job stops at <c>Subscriptions:PaymentWebhooks:MaxAttempts</c>.</param>
public record InboundWebhookEventDto(
    Guid Id,
    string Provider,
    string EventId,
    string Type,
    string? ProviderReference,
    decimal? Amount,
    string? Currency,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? LastAttemptAt,
    int Attempts,
    string? LastError);
