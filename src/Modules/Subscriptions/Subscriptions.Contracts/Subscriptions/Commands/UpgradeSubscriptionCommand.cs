using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands;

public record UpgradeSubscriptionCommand(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid NewPlanId) : ICommand<UpgradeSubscriptionResult>;

public record UpgradeSubscriptionResult(
    Guid SubscriptionId,
    string NewPlanName,
    decimal ProratedAmount,
    string Currency,
    bool IsDeferred,
    Guid InvoiceId,
    DateTimeOffset? ProofRequiredBy);
