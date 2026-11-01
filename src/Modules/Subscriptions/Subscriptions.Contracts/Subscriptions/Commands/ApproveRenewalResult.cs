namespace Subscriptions.Contracts.Subscriptions.Commands;

public record ApproveRenewalResult(
    Guid SubscriptionId,
    string Status,
    DateTimeOffset NewRenewalDate,
    string IdempotencyToken);
