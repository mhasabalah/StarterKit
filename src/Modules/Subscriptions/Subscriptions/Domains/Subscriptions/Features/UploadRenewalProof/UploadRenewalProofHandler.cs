using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.UploadRenewalProof;

public class UploadRenewalProofCommandValidator : AbstractValidator<UploadRenewalProofCommand>
{
    public UploadRenewalProofCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        this.AddProofFileRules(x => x.FileSize, x => x.ContentType);
    }
}

/// <summary>Renewal proof for a PastDue subscription.</summary>
public class UploadRenewalProofHandler(IProofSubmissionService proofSubmission)
    : ICommandHandler<UploadRenewalProofCommand, UploadPaymentProofResult>
{
    public Task<UploadPaymentProofResult> Handle(UploadRenewalProofCommand command, CancellationToken cancellationToken) =>
        proofSubmission.SubmitAsync(new ProofSubmissionRequest(
            command.SubscriptionId,
            command.SubscriberId,
            command.PaymentId,
            PaymentPurpose.Renewal,
            SubscriptionStatus.PastDue,
            command.FileStream,
            command.FileName,
            command.ContentType,
            command.FileSize,
            command.PaymentMethodId,
            "Subscription renewal payment proof"), cancellationToken);
}
