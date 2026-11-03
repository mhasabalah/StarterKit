namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="PaymentId">The activation payment to upload a proof for.</param>
/// <param name="Price">Plan price snapshot (tax basis per <c>Subscriptions:Tax:PricesIncludeTax</c>).</param>
/// <param name="AmountDue">Gross amount the subscriber must pay (activation payment amount).</param>
public record CreateSubscriptionResult(
    Guid SubscriptionId,
    Guid PaymentId,
    string Status,
    string PlanName,
    decimal Price,
    decimal AmountDue,
    string Currency,
    int BillingCycleInMonths);
