namespace Subscriptions.Contracts.Subscriptions.Commands;

public record CreateSubscriptionForApprovedApplicationCommand(
    Guid SubscriberId,
    Guid PlanId,
    string PlanName,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    string FeaturesJson,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    Guid? ProofAttachmentId,
    Guid? PaymentMethodId,
    string? PaymentMethodType,
    string? PaymentMethodDisplayName,
    string? PaymentMethodValue,
    string? PaymentMethodDescription,
    Guid AdminId
) : ICommand<Guid>;
