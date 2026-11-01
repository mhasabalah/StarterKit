using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.MarkRefundProcessed;

public class MarkRefundProcessedCommandValidator : AbstractValidator<MarkRefundProcessedCommand>
{
    public MarkRefundProcessedCommandValidator()
    {
        RuleFor(x => x.RefundRequestId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.ExternalTransactionRef).NotEmpty().MaximumLength(200);
    }
}

public class MarkRefundProcessedHandler(SubscriptionsDbContext dbContext)
    : ICommandHandler<MarkRefundProcessedCommand, Unit>
{
    public async Task<Unit> Handle(MarkRefundProcessedCommand command, CancellationToken cancellationToken)
    {
        var refundRequest = await dbContext.RefundRequests
            .FirstOrDefaultAsync(x => x.Id == command.RefundRequestId, cancellationToken)
            ?? throw new NotFoundException($"Refund request {command.RefundRequestId} not found.");

        refundRequest.MarkProcessed(command.AdminId, command.ExternalTransactionRef);

        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                "ProcessRefund",
                command.RefundRequestId,
                "RefundRequest",
                refundRequest.SubscriberId,
                command.ExternalTransactionRef),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
