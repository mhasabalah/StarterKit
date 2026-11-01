using Subscriptions.Contracts.Subscriptions.Events;
using Subscriptions.Domains.Subscriptions.Exceptions;

namespace Subscriptions.Domains.Subscriptions.Models;

public class SubscriptionPayment : Aggregate<Guid>
{
    public Guid SubscriptionId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public Guid? PaymentMethodId { get; private set; }
    public string? PaymentMethodType { get; private set; }
    public string? PaymentMethodDisplayName { get; private set; }
    public string? PaymentMethodValue { get; private set; }
    public string? PaymentMethodDescription { get; private set; }
    public Guid? ProofAttachmentId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? Notes { get; private set; }
    public string? IdempotencyToken { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    public Subscription Subscription { get; private set; } = null!;

    private SubscriptionPayment()
    {
    }

    public static SubscriptionPayment Create(Guid subscriptionId, decimal amount, string currency)
    {
        return new SubscriptionPayment
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void SubmitProof(
        Guid attachmentId,
        PaymentMethod paymentMethod,
        Guid subscriberId)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Rejected))
        {
            throw new ConflictException(
                $"Cannot transition payment from {Status} to {PaymentStatus.Submitted}.");
        }

        ProofAttachmentId = attachmentId;
        ApplyPaymentMethodSnapshot(paymentMethod);
        Status = PaymentStatus.Submitted;
        ReviewedBy = null;
        ReviewedAt = null;
        Notes = null;
        AddDomainEvent(new PaymentSubmittedEvent(Id, SubscriptionId, subscriberId));
    }

    public void SubmitProofWithSnapshot(
        Guid attachmentId,
        Guid? paymentMethodId,
        string? paymentMethodType,
        string? paymentMethodDisplayName,
        string? paymentMethodValue,
        string? paymentMethodDescription,
        Guid subscriberId)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Rejected))
        {
            throw new ConflictException(
                $"Cannot transition payment from {Status} to {PaymentStatus.Submitted}.");
        }

        ProofAttachmentId = attachmentId;
        PaymentMethodId = paymentMethodId;
        PaymentMethodType = paymentMethodType;
        PaymentMethodDisplayName = paymentMethodDisplayName;
        PaymentMethodValue = paymentMethodValue;
        PaymentMethodDescription = paymentMethodDescription;
        Status = PaymentStatus.Submitted;
        ReviewedBy = null;
        ReviewedAt = null;
        Notes = null;
        AddDomainEvent(new PaymentSubmittedEvent(Id, SubscriptionId, subscriberId));
    }

    private void ApplyPaymentMethodSnapshot(PaymentMethod paymentMethod)
    {
        PaymentMethodId = paymentMethod.Id;
        PaymentMethodType = paymentMethod.Type;
        PaymentMethodDisplayName = paymentMethod.DisplayName;
        PaymentMethodValue = paymentMethod.Value;
        PaymentMethodDescription = paymentMethod.Description;
    }

    public void Approve(Guid adminId, Guid subscriberId)
    {
        if (Status != PaymentStatus.Submitted)
        {
            throw new ConflictException($"Cannot transition payment from {Status} to {PaymentStatus.Paid}.");
        }

        Status = PaymentStatus.Paid;
        PaidAt = DateTimeOffset.UtcNow;
        ReviewedBy = adminId;
        ReviewedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new PaymentApprovedEvent(Id, SubscriptionId, subscriberId));
    }

    public void Reject(Guid adminId, string reason, Guid subscriberId)
    {
        if (Status != PaymentStatus.Submitted)
        {
            throw new ConflictException(
                $"Cannot transition payment from {Status} to {PaymentStatus.Rejected}.");
        }

        Status = PaymentStatus.Rejected;
        ReviewedBy = adminId;
        ReviewedAt = DateTimeOffset.UtcNow;
        Notes = reason;
        AddDomainEvent(new PaymentRejectedEvent(Id, SubscriptionId, subscriberId, reason));
    }

    public void MarkFailed()
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Submitted))
        {
            return;
        }

        Status = PaymentStatus.Failed;
    }
}
