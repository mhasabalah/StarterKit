namespace Subscriptions.Contracts.Subscriptions.Commands;

public record UpgradeSubscriptionCommand(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid NewPlanId) : ICommand<UpgradeSubscriptionResult>;

/// <param name="ProratedAmount">Prorated price difference for the rest of the current period (tax basis).</param>
/// <param name="AmountDue">Gross amount of the proration payment (0 when deferred or nothing is due).</param>
/// <param name="IsDeferred">True when the proration is added to the next renewal instead of being charged now.</param>
/// <param name="PaymentId">The proration payment to upload a proof for (null when deferred or nothing is due).</param>
/// <param name="InvoiceId">The proration invoice (null when deferred or nothing is due).</param>
/// <param name="ProofRequiredBy">Proof upload + approval deadline; after it an unpaid upgrade is reverted.</param>
public record UpgradeSubscriptionResult(
    Guid SubscriptionId,
    string NewPlanName,
    decimal ProratedAmount,
    decimal AmountDue,
    string Currency,
    bool IsDeferred,
    Guid? PaymentId,
    Guid? InvoiceId,
    DateTimeOffset? ProofRequiredBy);
