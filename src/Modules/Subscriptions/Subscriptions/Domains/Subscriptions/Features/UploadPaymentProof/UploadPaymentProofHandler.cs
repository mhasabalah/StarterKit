using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.UploadPaymentProof;

public class UploadPaymentProofCommandValidator : AbstractValidator<UploadPaymentProofCommand>
{
    public UploadPaymentProofCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        this.AddProofFileRules(x => x.FileSize, x => x.ContentType);
    }
}

/// <summary>Activation proof for a PendingApproval subscription.</summary>
public class UploadPaymentProofHandler(IProofSubmissionService proofSubmission)
    : ICommandHandler<UploadPaymentProofCommand, UploadPaymentProofResult>
{
    public Task<UploadPaymentProofResult> Handle(UploadPaymentProofCommand command, CancellationToken cancellationToken) =>
        proofSubmission.SubmitAsync(new ProofSubmissionRequest(
            command.SubscriptionId,
            command.SubscriberId,
            command.PaymentId,
            PaymentPurpose.Activation,
            SubscriptionStatus.PendingApproval,
            command.FileStream,
            command.FileName,
            command.ContentType,
            command.FileSize,
            command.PaymentMethodId,
            "Subscription payment proof"), cancellationToken);
}
