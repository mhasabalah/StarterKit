using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.UploadPaymentProof;

public class UploadPaymentProofCommandValidator : AbstractValidator<UploadPaymentProofCommand>
{
    public UploadPaymentProofCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        RuleFor(x => x.FileSize).GreaterThan(0).LessThanOrEqualTo(10 * 1024 * 1024);
        RuleFor(x => x.ContentType)
            .Must(x => x is "image/jpeg" or "image/png" or "application/pdf");
    }
}

public class UploadPaymentProofHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentMethodRepository paymentMethodRepository,
    ISender sender)
    : ICommandHandler<UploadPaymentProofCommand, UploadPaymentProofResult>
{
    public async Task<UploadPaymentProofResult> Handle(
        UploadPaymentProofCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to upload proof for this subscription.");

        if (subscription.Status != SubscriptionStatus.PendingApproval)
            throw new ConflictException("Subscription is not pending approval.");

        var payment = subscription.Payments
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault(x =>
                !command.PaymentId.HasValue || command.PaymentId.Value == Guid.Empty || x.Id == command.PaymentId.Value)
            ?? throw new PaymentNotFoundException(command.PaymentId ?? command.SubscriptionId);

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Rejected))
            throw new ConflictException("Payment status is not valid for proof upload.");

        var paymentMethod = await paymentMethodRepository.GetByIdAsync(command.PaymentMethodId, cancellationToken)
            ?? throw new PaymentMethodNotFoundException(command.PaymentMethodId);

        if (!paymentMethod.IsActive)
            throw new BadRequestException("Payment method is not active.");

        var uploadResult = await sender.Send(new UploadAttachmentCommand(
            FileName: command.FileName,
            ContentType: command.ContentType,
            FileSize: command.FileSize,
            FileStream: command.FileStream,
            Type: AttachmentType.Document,
            EntityType: EntityType.Other,
            EntityId: command.SubscriptionId,
            Description: "Subscription payment proof",
            IsPublic: false,
            GenerateThumbnail: false), cancellationToken);

        payment.SubmitProof(uploadResult.Id, paymentMethod, command.SubscriberId);
        await subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        return new UploadPaymentProofResult(payment.Id, payment.Status.ToString());
    }
}
