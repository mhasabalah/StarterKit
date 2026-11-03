namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <param name="Purpose">Activation, Renewal or Proration: tells the admin which approve endpoint to call.</param>
/// <param name="Version">The payment version (round-trip as the proration approve/reject Version).</param>
/// <param name="SubscriptionVersion">The subscription version (round-trip as the activation/renewal approve/reject Version).</param>
/// <param name="SubscriberName">Subscriber name snapshot (denormalized on the subscription).</param>
/// <param name="AgeHours">Hours since the proof was submitted (queue age).</param>
/// <param name="DuplicateProof">The proof file has the same SHA-256 as another payment proof (warning only).</param>
/// <param name="AmountReceived">Already received through manual receipts (the proof covers the rest).</param>
public record PendingPaymentDto(
    Guid PaymentId,
    Guid SubscriptionId,
    Guid SubscriberId,
    decimal Amount,
    string Currency,
    PaymentMethodSnapshotDto? PaymentMethod,
    DateTimeOffset SubmittedAt,
    string Note,
    string Purpose = "",
    long Version = 0,
    long SubscriptionVersion = 0,
    string? SubscriberName = null,
    string? SubscriberEmail = null,
    Guid? PlanId = null,
    string? PlanName = null,
    double AgeHours = 0,
    bool DuplicateProof = false,
    Guid? DuplicateOfPaymentId = null,
    Guid? ProofAttachmentId = null,
    decimal AmountReceived = 0,
    decimal OutstandingAmount = 0);
