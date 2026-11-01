namespace Subscriptions.Contracts.Subscriptions.Commands;

public record AdminReviewRefundCommand(
    Guid RefundRequestId,
    Guid AdminId,
    bool Approve,
    string? Notes) : ICommand;
