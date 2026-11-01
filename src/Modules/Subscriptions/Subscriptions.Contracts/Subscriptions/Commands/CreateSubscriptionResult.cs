namespace Subscriptions.Contracts.Subscriptions.Commands;

public record CreateSubscriptionResult(
    Guid SubscriptionId,
    string IdempotencyToken,
    string Status,
    string PlanName,
    decimal Price,
    string Currency,
    int BillingCycleInMonths);
