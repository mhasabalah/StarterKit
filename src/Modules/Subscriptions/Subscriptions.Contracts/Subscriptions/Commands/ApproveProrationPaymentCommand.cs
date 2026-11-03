namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>Admin approves the submitted proof of an upgrade proration payment of an Active subscription.</summary>
/// <param name="Version">The PAYMENT version the admin saw (<c>PendingPaymentDto.Version</c>).</param>
public record ApproveProrationPaymentCommand(
    Guid SubscriptionId,
    Guid PaymentId,
    Guid AdminId,
    long? Version = null) : ICommand<ApproveProrationPaymentResult>;

public record ApproveProrationPaymentResult(
    Guid SubscriptionId,
    Guid PaymentId,
    string PaymentStatus,
    Guid InvoiceId,
    string InvoiceNumber);
