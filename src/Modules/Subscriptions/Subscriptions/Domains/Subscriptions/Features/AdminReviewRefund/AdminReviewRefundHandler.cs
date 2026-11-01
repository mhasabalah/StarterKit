using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.AdminReviewRefund;

public class AdminReviewRefundCommandValidator : AbstractValidator<AdminReviewRefundCommand>
{
    public AdminReviewRefundCommandValidator()
    {
        RuleFor(x => x.RefundRequestId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Notes)
            .NotEmpty()
            .When(x => !x.Approve)
            .WithMessage("Rejection reason is required.");
    }
}

public class AdminReviewRefundHandler(SubscriptionsDbContext dbContext)
    : ICommandHandler<AdminReviewRefundCommand, Unit>
{
    public async Task<Unit> Handle(AdminReviewRefundCommand command, CancellationToken cancellationToken)
    {
        var refundRequest = await dbContext.RefundRequests
            .FirstOrDefaultAsync(x => x.Id == command.RefundRequestId, cancellationToken)
            ?? throw new NotFoundException($"Refund request {command.RefundRequestId} not found.");

        if (command.Approve)
            refundRequest.Approve(command.AdminId, command.Notes);
        else
            refundRequest.Reject(command.AdminId, command.Notes!);

        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                command.Approve ? "ApproveRefund" : "RejectRefund",
                command.RefundRequestId,
                "RefundRequest",
                refundRequest.SubscriberId,
                command.Notes),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
