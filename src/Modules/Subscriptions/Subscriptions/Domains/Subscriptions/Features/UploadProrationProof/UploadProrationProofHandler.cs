using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.UploadProrationProof;

public class UploadProrationProofCommandValidator : AbstractValidator<UploadProrationProofCommand>
{
    public UploadProrationProofCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        this.AddProofFileRules(x => x.FileSize, x => x.ContentType);
    }
}

/// <summary>Upgrade proration proof for an Active subscription, keyed by the specific proration payment.</summary>
public class UploadProrationProofHandler(IProofSubmissionService proofSubmission)
    : ICommandHandler<UploadProrationProofCommand, UploadPaymentProofResult>
{
    public Task<UploadPaymentProofResult> Handle(UploadProrationProofCommand command, CancellationToken cancellationToken) =>
        proofSubmission.SubmitAsync(new ProofSubmissionRequest(
            command.SubscriptionId,
            command.SubscriberId,
            command.PaymentId,
            PaymentPurpose.Proration,
            SubscriptionStatus.Active,
            command.FileStream,
            command.FileName,
            command.ContentType,
            command.FileSize,
            command.PaymentMethodId,
            "Subscription upgrade proration payment proof"), cancellationToken);
}
