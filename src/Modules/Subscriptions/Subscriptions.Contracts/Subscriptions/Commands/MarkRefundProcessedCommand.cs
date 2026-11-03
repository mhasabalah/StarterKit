namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="Version">The refund request version the admin saw.</param>
public record MarkRefundProcessedCommand(
    Guid RefundRequestId,
    Guid AdminId,
    string ExternalTransactionRef,
    long? Version = null) : ICommand;
