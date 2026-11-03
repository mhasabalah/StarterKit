namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="Version">The refund request version the admin saw (<c>AdminRefundRequestDto.Version</c>).</param>
public record AdminReviewRefundCommand(
    Guid RefundRequestId,
    Guid AdminId,
    bool Approve,
    string? Notes,
    long? Version = null) : ICommand;
