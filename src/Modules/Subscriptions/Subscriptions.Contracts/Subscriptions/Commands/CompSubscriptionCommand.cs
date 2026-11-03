namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Admin grants a complimentary (zero-price) activation on <paramref name="PlanId"/> for <paramref name="Months"/>
/// months: PendingApproval → Active (the owed activation payment is waived), or a recently Cancelled/Expired
/// subscription → Active. Issues an invoice with a 0 total and a ledger Adjustment of 0. The next renewal is billed
/// normally. Audited.
/// </summary>
/// <param name="Months">1-36.</param>
/// <param name="Version">The subscription version the admin saw (stale = 409).</param>
public record CompSubscriptionCommand(
    Guid SubscriptionId,
    Guid AdminId,
    Guid PlanId,
    int Months,
    string Reason,
    long? Version = null) : ICommand<CompSubscriptionResult>;

public record CompSubscriptionResult(
    Guid SubscriptionId,
    string Status,
    Guid PlanId,
    string PlanName,
    DateTimeOffset StartDate,
    DateTimeOffset RenewalDate,
    Guid PaymentId,
    Guid InvoiceId,
    string InvoiceNumber);
