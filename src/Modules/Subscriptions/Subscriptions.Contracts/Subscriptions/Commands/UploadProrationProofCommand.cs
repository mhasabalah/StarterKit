namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>Subscriber uploads the payment proof for one specific upgrade proration payment.</summary>
public record UploadProrationProofCommand(
    Guid SubscriptionId,
    Guid PaymentId,
    Guid SubscriberId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize,
    Guid PaymentMethodId) : ICommand<UploadPaymentProofResult>;
