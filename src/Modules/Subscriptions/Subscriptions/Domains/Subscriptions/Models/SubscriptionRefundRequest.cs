using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Events;

namespace Subscriptions.Domains.Subscriptions.Models;

public class SubscriptionRefundRequest : Aggregate<Guid>
{
    public Guid SubscriptionId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public Guid PlanId { get; private set; }
    public string PlanName { get; private set; } = default!;
    public int BillingCycleInMonths { get; private set; }
    public DateTimeOffset SubscriptionStartedAt { get; private set; }
    public DateTimeOffset CancelledAt { get; private set; }
    public DateTimeOffset RenewalDate { get; private set; }
    public decimal RequestedAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public RefundType RefundType { get; private set; }
    public RefundStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ExternalTransactionRef { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SubscriptionRefundRequest() { }

    public static SubscriptionRefundRequest Create(
        Guid subscriptionId,
        Guid subscriberId,
        Guid planId,
        string planName,
        int billingCycleInMonths,
        DateTimeOffset subscriptionStartedAt,
        DateTimeOffset cancelledAt,
        DateTimeOffset renewalDate,
        decimal requestedAmount,
        string currency,
        RefundType refundType)
    {
        var request = new SubscriptionRefundRequest
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            SubscriberId = subscriberId,
            PlanId = planId,
            PlanName = planName,
            BillingCycleInMonths = billingCycleInMonths,
            SubscriptionStartedAt = subscriptionStartedAt,
            CancelledAt = cancelledAt,
            RenewalDate = renewalDate,
            RequestedAmount = requestedAmount,
            Currency = currency,
            RefundType = refundType,
            Status = RefundStatus.Requested,
            RequestedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        request.AddDomainEvent(new RefundRequestedEvent(
            request.Id, subscriptionId, subscriberId, requestedAmount, currency));

        return request;
    }

    public void MarkUnderReview(Guid adminId)
    {
        if (Status != RefundStatus.Requested)
            throw new ConflictException("Only requested refunds can be marked under review.");

        Status = RefundStatus.UnderReview;
        ReviewedBy = adminId;
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(Guid adminId, string? notes)
    {
        if (Status is not (RefundStatus.Requested or RefundStatus.UnderReview))
            throw new ConflictException("Only requested or under-review refunds can be approved.");

        Status = RefundStatus.Approved;
        ReviewedBy = adminId;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewNotes = notes;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new RefundApprovedEvent(Id, SubscriptionId, SubscriberId, RequestedAmount, Currency));
    }

    public void Reject(Guid adminId, string reason)
    {
        if (Status is not (RefundStatus.Requested or RefundStatus.UnderReview))
            throw new ConflictException("Only requested or under-review refunds can be rejected.");

        Status = RefundStatus.Rejected;
        ReviewedBy = adminId;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewNotes = reason;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new RefundRejectedEvent(Id, SubscriptionId, SubscriberId, reason));
    }

    public void MarkProcessed(Guid adminId, string externalTransactionRef)
    {
        if (Status != RefundStatus.Approved)
            throw new ConflictException("Only approved refunds can be marked as processed.");

        Status = RefundStatus.Processed;
        ProcessedAt = DateTimeOffset.UtcNow;
        ExternalTransactionRef = externalTransactionRef;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new RefundProcessedEvent(Id, SubscriptionId, SubscriberId, RequestedAmount, Currency, externalTransactionRef));
    }
}
