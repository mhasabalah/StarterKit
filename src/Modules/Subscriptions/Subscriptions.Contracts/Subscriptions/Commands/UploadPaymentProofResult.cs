namespace Subscriptions.Contracts.Subscriptions.Commands;

public record UploadPaymentProofResult(Guid PaymentId, string Status);
