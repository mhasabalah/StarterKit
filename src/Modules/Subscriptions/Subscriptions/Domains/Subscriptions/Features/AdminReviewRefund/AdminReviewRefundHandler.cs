using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Features.RequestRefund;
using Subscriptions.Domains.Subscriptions.Services;

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
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class AdminReviewRefundHandler(SubscriptionsDbContext dbContext, IAdminAuditWriter audit)
    : ICommandHandler<AdminReviewRefundCommand, Unit>
{
    public async Task<Unit> Handle(AdminReviewRefundCommand command, CancellationToken cancellationToken)
    {
        var refundRequest = await dbContext.RefundRequests
            .FirstOrDefaultAsync(x => x.Id == command.RefundRequestId, cancellationToken)
            ?? throw new NotFoundException($"Refund request {command.RefundRequestId} not found.");

        dbContext.ExpectVersion(refundRequest, command.Version);
        var before = refundRequest.Status;

        if (command.Approve)
        {
            // Re-check the cap at approval time: money may have been refunded since the request.
            var refundable = await RefundInputs.RefundableAsync(
                dbContext, refundRequest.SubscriptionId, null, refundRequest.Id, cancellationToken);
            if (refundRequest.RequestedAmount > refundable)
                throw new ConflictException($"The refund exceeds the refundable amount ({CurrencyRules.FormatWithCode(refundable, refundRequest.Currency)}).");

            refundRequest.Approve(command.AdminId, command.Notes);
        }
        else
        {
            refundRequest.Reject(command.AdminId, command.Notes!);
        }

        audit.Record(
            command.Approve ? "ApproveRefund" : "RejectRefund",
            refundRequest.Id,
            "RefundRequest",
            refundRequest.SubscriberId,
            command.Notes,
            refundRequest.RequestedAmount,
            refundRequest.Currency,
            before: new { Status = before.ToString() },
            after: new { Status = refundRequest.Status.ToString() },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
