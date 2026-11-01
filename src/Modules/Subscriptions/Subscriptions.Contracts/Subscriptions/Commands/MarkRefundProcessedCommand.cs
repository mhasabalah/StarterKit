namespace Subscriptions.Contracts.Subscriptions.Commands;

public record MarkRefundProcessedCommand(
    Guid RefundRequestId,
    Guid AdminId,
    string ExternalTransactionRef) : ICommand;
