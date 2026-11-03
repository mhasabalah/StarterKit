namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <param name="Version">Round-trip as <c>AdminReviewRefundCommand.Version</c> / <c>MarkRefundProcessedCommand.Version</c>.</param>
public record AdminRefundRequestDto(
    Guid RefundRequestId,
    Guid SubscriptionId,
    Guid SubscriberId,
    string PlanName,
    int BillingCycleInMonths,
    DateTimeOffset SubscriptionStartedAt,
    DateTimeOffset CancelledAt,
    DateTimeOffset RenewalDate,
    decimal RequestedAmount,
    string Currency,
    string RefundType,
    string Status,
    DateTimeOffset RequestedAt,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewNotes,
    DateTimeOffset? ProcessedAt,
    string? ExternalTransactionRef,
    long Version = 0);
