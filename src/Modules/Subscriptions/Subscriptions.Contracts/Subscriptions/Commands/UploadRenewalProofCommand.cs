namespace Subscriptions.Contracts.Subscriptions.Commands;

public record UploadRenewalProofCommand(
    Guid SubscriptionId,
    Guid? PaymentId,
    Guid SubscriberId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize,
    Guid PaymentMethodId) : ICommand<UploadPaymentProofResult>;
