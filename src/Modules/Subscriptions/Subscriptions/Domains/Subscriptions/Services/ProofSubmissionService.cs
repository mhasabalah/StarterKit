using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>A subscriber's payment proof upload for one payment of the given purpose.</summary>
public sealed record ProofSubmissionRequest(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid? PaymentId,
    PaymentPurpose Purpose,
    SubscriptionStatus RequiredStatus,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize,
    Guid PaymentMethodId,
    string Description);

/// <summary>
/// The ONE proof-upload flow shared by activation, renewal and upgrade-proration proofs: ownership + status checks,
/// payment resolution, active payment method, private attachment upload, payment → Submitted.
/// </summary>
public interface IProofSubmissionService
{
    Task<UploadPaymentProofResult> SubmitAsync(ProofSubmissionRequest request, CancellationToken ct = default);
}

public sealed class ProofSubmissionService(
    ISubscriptionRepository subscriptionRepository,
    IPaymentMethodRepository paymentMethodRepository,
    ISender sender,
    ILogger<ProofSubmissionService> logger)
    : IProofSubmissionService
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    public static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "application/pdf"];

    public async Task<UploadPaymentProofResult> SubmitAsync(ProofSubmissionRequest request, CancellationToken ct = default)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(request.SubscriptionId, ct)
            ?? throw new SubscriptionNotFoundException(request.SubscriptionId);

        if (subscription.SubscriberId != request.SubscriberId)
            throw new ForbiddenException("You are not allowed to upload proof for this subscription.");

        if (subscription.Status != request.RequiredStatus)
            throw new ConflictException($"Subscription must be {request.RequiredStatus} to upload this proof.");

        var payment = await ResolvePaymentAsync(request, ct);

        if (!payment.AcceptsProof)
            throw new ConflictException("Payment status is not valid for proof upload.");

        var paymentMethod = await paymentMethodRepository.GetByIdAsync(request.PaymentMethodId, ct)
            ?? throw new PaymentMethodNotFoundException(request.PaymentMethodId);

        if (!paymentMethod.IsActive)
            throw new BadRequestException("Payment method is not active.");

        var upload = await sender.Send(new UploadAttachmentCommand(
            FileName: request.FileName,
            ContentType: request.ContentType,
            FileSize: request.FileSize,
            FileStream: request.FileStream,
            Type: AttachmentType.Document,
            EntityType: EntityType.Other,
            EntityId: request.SubscriptionId,
            Description: request.Description,
            IsPublic: false,
            GenerateThumbnail: false), ct);

        var contentHash = await ReadContentHashAsync(upload.Id, ct);
        var duplicateOf = contentHash is null
            ? null
            : await subscriptionRepository.FindPaymentWithProofHashAsync(contentHash, payment.Id, ct);

        if (duplicateOf is not null)
        {
            // Never blocks: the reviewer sees the flag in the queue / Subscriber-360 / approval audit.
            logger.LogWarning(
                "Proof for payment {PaymentId} has the same content as the proof of payment {DuplicateOfPaymentId}",
                payment.Id, duplicateOf);
        }

        payment.SubmitProof(
            upload.Id,
            PaymentMethodSnapshot.From(paymentMethod),
            request.SubscriberId,
            DateTimeOffset.UtcNow,
            contentHash,
            duplicateOf);
        await subscriptionRepository.SaveChangesAsync(ct);

        return new UploadPaymentProofResult(payment.Id, payment.Status.ToString());
    }

    /// <summary>
    /// SHA-256 of the stored proof (Attachment computes it on upload). Best effort: a missing hash only disables the
    /// duplicate check, it never fails the upload.
    /// </summary>
    private async Task<string?> ReadContentHashAsync(Guid attachmentId, CancellationToken ct)
    {
        try
        {
            var attachment = await sender.Send(new GetAttachmentByIdQuery(attachmentId), ct);
            var hash = attachment.Attachment.ContentHash;
            return string.IsNullOrWhiteSpace(hash) ? null : hash.ToLowerInvariant();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the content hash of proof attachment {AttachmentId}", attachmentId);
            return null;
        }
    }

    private async Task<SubscriptionPayment> ResolvePaymentAsync(ProofSubmissionRequest request, CancellationToken ct)
    {
        if (request.PaymentId is { } id && id != Guid.Empty)
        {
            var payment = await subscriptionRepository.GetPaymentAsync(request.SubscriptionId, id, ct)
                ?? throw new PaymentNotFoundException(id);

            if (payment.Purpose != request.Purpose)
                throw new BadRequestException($"Payment '{id}' is a {payment.Purpose} payment, not a {request.Purpose} payment.");

            return payment;
        }

        var candidates = (await subscriptionRepository.GetAwaitingPaymentsAsync(request.SubscriptionId, ct))
            .Where(x => x.Purpose == request.Purpose && x.AcceptsProof)
            .ToList();

        return candidates.Count switch
        {
            0 => throw new PaymentNotFoundException(request.SubscriptionId),
            1 => candidates[0],
            _ => throw new AmbiguousPaymentException()
        };
    }
}

/// <summary>Validation rules shared by the three proof-upload command validators.</summary>
public static class ProofUploadRules
{
    public static void AddProofFileRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, long>> fileSize,
        System.Linq.Expressions.Expression<Func<T, string>> contentType)
    {
        validator.RuleFor(fileSize).GreaterThan(0).LessThanOrEqualTo(ProofSubmissionService.MaxFileSize);
        validator.RuleFor(contentType)
            .Must(x => ProofSubmissionService.AllowedContentTypes.Contains(x))
            .WithMessage("Proof must be a JPEG, PNG or PDF file.");
    }
}
