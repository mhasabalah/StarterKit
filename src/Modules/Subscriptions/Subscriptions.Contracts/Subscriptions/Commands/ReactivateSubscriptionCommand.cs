namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Admin reopens a Cancelled/Expired subscription that ended within <c>Subscriptions:ReactivationWindowDays</c>.
/// Default: back to PendingApproval with a NEW activation payment at the current plan price (approve its proof or
/// record a manual payment to activate). <paramref name="Complimentary"/>: straight to Active for
/// <paramref name="Months"/> months at zero price (same as the comp action).
/// </summary>
/// <param name="Months">Complimentary months (required when <paramref name="Complimentary"/>, 1-36).</param>
/// <param name="Version">The subscription version the admin saw (stale = 409).</param>
public record ReactivateSubscriptionCommand(
    Guid SubscriptionId,
    Guid AdminId,
    string Reason,
    bool Complimentary = false,
    int? Months = null,
    long? Version = null) : ICommand<ReactivateSubscriptionResult>;

/// <param name="PaymentId">The new activation payment (PendingApproval) or the zero-amount comp payment (Active).</param>
public record ReactivateSubscriptionResult(
    Guid SubscriptionId,
    string Status,
    Guid PaymentId,
    decimal AmountDue,
    string Currency,
    DateTimeOffset? RenewalDate,
    Guid? InvoiceId,
    string? InvoiceNumber);
