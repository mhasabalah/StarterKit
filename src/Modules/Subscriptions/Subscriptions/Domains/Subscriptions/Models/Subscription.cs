using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Events;
using Subscriptions.Domains.Subscriptions.Exceptions;

namespace Subscriptions.Domains.Subscriptions.Models;

public class Subscription : Aggregate<Guid>
{
    private readonly List<SubscriptionPayment> _payments = [];
    private readonly List<SubscriptionInvoice> _invoices = [];

    public Guid SubscriberId { get; private set; }
    public Guid PlanId { get; private set; }
    public string PlanName { get; private set; } = default!;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = default!;
    public int BillingCycleInMonths { get; private set; }
    public int? MaxItems { get; private set; }
    public int? MaxStorageGb { get; private set; }
    public int? MaxUsers { get; private set; }
    public string FeaturesJson { get; private set; } = "{}";
    public SubscriptionStatus Status { get; private set; }
    public string? IdempotencyToken { get; private set; }
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? RenewalDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }
    public DateTimeOffset? GracePeriodEndsAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public bool CancelledAtPeriodEnd { get; private set; }
    public DateTimeOffset? ScheduledCancellationDate { get; private set; }
    public CancellationReason? CancellationReason { get; private set; }
    public string? CancellationNote { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ScheduledPlanChange? ScheduledPlanChange { get; private set; }
    public IReadOnlyList<SubscriptionPayment> Payments => _payments.AsReadOnly();
    public IReadOnlyList<SubscriptionInvoice> Invoices => _invoices.AsReadOnly();

    private Subscription()
    {
    }

    public static Subscription Create(
        Guid subscriberId,
        Plan plan,
        string featuresJson,
        string? idempotencyToken)
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriberId = subscriberId,
            PlanId = plan.Id,
            PlanName = plan.Name.En,
            Price = plan.Price,
            Currency = plan.Currency,
            BillingCycleInMonths = plan.BillingCycleInMonths,
            MaxItems = plan.MaxItems,
            MaxStorageGb = plan.MaxStorageGb,
            MaxUsers = plan.MaxUsers,
            FeaturesJson = featuresJson,
            Status = SubscriptionStatus.PendingApproval,
            IdempotencyToken = idempotencyToken,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        subscription.AddDomainEvent(new SubscriptionCreatedEvent(
            subscription.Id,
            subscription.SubscriberId,
            subscription.PlanId,
            subscription.PlanName));

        return subscription;
    }

    public void AddPayment(SubscriptionPayment payment) => _payments.Add(payment);

    public void AddInvoice(SubscriptionInvoice invoice) => _invoices.Add(invoice);

    public void Activate()
    {
        if (Status != SubscriptionStatus.PendingApproval)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);

        Status = SubscriptionStatus.Active;
        StartDate = DateTimeOffset.UtcNow;
        RenewalDate = DateTimeOffset.UtcNow.AddMonths(BillingCycleInMonths);
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionActivatedEvent(Id, SubscriberId, StartDate.Value, RenewalDate.Value));
    }

    public void Reject(string? reason)
    {
        if (Status != SubscriptionStatus.PendingApproval)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Rejected);

        Status = SubscriptionStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionRejectedEvent(Id, SubscriberId, reason));
    }

    public void CancelImmediately(Guid actorId, CancellationReason? reason, string? note)
    {
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Cancelled);

        if (ScheduledPlanChange is { Status: PlanChangeStatus.Pending })
            ScheduledPlanChange.Cancel();

        CancellationReason = reason;
        CancellationNote = note;
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        EndDate = DateTimeOffset.UtcNow;
        CancelledAtPeriodEnd = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionCancelledEvent(Id, SubscriberId, CancelledAt.Value, actorId, reason, IsScheduled: false));
    }

    public void ScheduleCancellation(Guid actorId, CancellationReason? reason, string? note)
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Cancelled);

        if (CancelledAtPeriodEnd)
            throw new ConflictException("A cancellation is already scheduled for end of period.");

        CancellationReason = reason;
        CancellationNote = note;
        CancelledAt = DateTimeOffset.UtcNow;
        CancelledAtPeriodEnd = true;
        ScheduledCancellationDate = RenewalDate;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionCancellationScheduledEvent(Id, SubscriberId, actorId, ScheduledCancellationDate!.Value, reason));
    }

    public void ApplyScheduledCancellation(Guid systemActorId)
    {
        if (!CancelledAtPeriodEnd || Status != SubscriptionStatus.Active)
            throw new ConflictException("No scheduled cancellation to apply.");

        if (ScheduledPlanChange is { Status: PlanChangeStatus.Pending })
            ScheduledPlanChange.Cancel();

        Status = SubscriptionStatus.Cancelled;
        EndDate = DateTimeOffset.UtcNow;
        CancelledAtPeriodEnd = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionCancelledEvent(Id, SubscriberId, EndDate.Value, systemActorId, CancellationReason, IsScheduled: true));
    }

    public SubscriptionPayment MarkPastDue(int gracePeriodDays = 7, decimal? effectivePrice = null)
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.PastDue);

        var paymentAmount = effectivePrice ?? Price;

        Status = SubscriptionStatus.PastDue;
        GracePeriodEndsAt = DateTimeOffset.UtcNow.AddDays(gracePeriodDays);
        UpdatedAt = DateTimeOffset.UtcNow;
        var newPayment = SubscriptionPayment.Create(Id, paymentAmount, Currency);
        _payments.Add(newPayment);
        AddDomainEvent(new SubscriptionPastDueEvent(Id, SubscriberId, GracePeriodEndsAt.Value, newPayment.Id));

        return newPayment;
    }

    public void Renew()
    {
        if (Status != SubscriptionStatus.PastDue)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);

        Status = SubscriptionStatus.Active;
        RenewalDate = DateTimeOffset.UtcNow.AddMonths(BillingCycleInMonths);
        GracePeriodEndsAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionRenewedEvent(Id, SubscriberId, RenewalDate.Value));
    }

    public void UpdatePrice(decimal newPrice)
    {
        Price = newPrice;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        if (Status != SubscriptionStatus.PastDue)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Expired);

        Status = SubscriptionStatus.Expired;
        EndDate = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionExpiredEvent(Id, SubscriberId));
    }

    public void UpgradePlan(Plan newPlan, string newFeaturesJson, Guid actorId, decimal proratedAmount, bool isDeferred)
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);

        var oldPlanId = PlanId;

        PlanId = newPlan.Id;
        PlanName = newPlan.Name.En;
        Price = newPlan.Price;
        Currency = newPlan.Currency;
        BillingCycleInMonths = newPlan.BillingCycleInMonths;
        MaxItems = newPlan.MaxItems;
        MaxStorageGb = newPlan.MaxStorageGb;
        MaxUsers = newPlan.MaxUsers;
        FeaturesJson = newFeaturesJson;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new SubscriptionUpgradedEvent(Id, SubscriberId, oldPlanId, PlanId, proratedAmount, isDeferred));
    }

    public void RevertUpgrade(Plan originalPlan, string originalFeaturesJson, string reason)
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);

        PlanId = originalPlan.Id;
        PlanName = originalPlan.Name.En;
        Price = originalPlan.Price;
        Currency = originalPlan.Currency;
        BillingCycleInMonths = originalPlan.BillingCycleInMonths;
        MaxItems = originalPlan.MaxItems;
        MaxStorageGb = originalPlan.MaxStorageGb;
        MaxUsers = originalPlan.MaxUsers;
        FeaturesJson = originalFeaturesJson;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new SubscriptionUpgradeRevertedEvent(Id, SubscriberId, originalPlan.Id));
    }

    public void ScheduleDowngrade(Plan newPlan, string newFeaturesJson, Guid scheduledBy)
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);

        var effectiveAt = RenewalDate ?? DateTimeOffset.UtcNow.AddMonths(BillingCycleInMonths);
        ScheduledPlanChange = ScheduledPlanChange.Create(
            Id,
            PlanId,
            newPlan,
            newFeaturesJson,
            PlanChangeType.Downgrade,
            scheduledBy,
            effectiveAt);

        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionDowngradeScheduledEvent(Id, SubscriberId, newPlan.Id, effectiveAt));
    }

    public void CancelScheduledDowngrade(Guid actorId)
    {
        if (ScheduledPlanChange is null || ScheduledPlanChange.Status != PlanChangeStatus.Pending)
            throw new ConflictException("No pending scheduled downgrade exists.");

        ScheduledPlanChange.Cancel();
        UpdatedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new SubscriptionDowngradeCancelledEvent(Id, SubscriberId));
    }

    public void ApplyScheduledDowngrade()
    {
        if (ScheduledPlanChange is null ||
            ScheduledPlanChange.Status != PlanChangeStatus.Pending ||
            ScheduledPlanChange.EffectiveAt > DateTimeOffset.UtcNow)
        {
            throw new ConflictException("No applicable scheduled downgrade found.");
        }

        PlanId = ScheduledPlanChange.NewPlanId;
        PlanName = ScheduledPlanChange.NewPlanName;
        Price = ScheduledPlanChange.NewPrice;
        BillingCycleInMonths = ScheduledPlanChange.NewBillingCycleInMonths;
        MaxItems = ScheduledPlanChange.NewMaxItems;
        MaxStorageGb = ScheduledPlanChange.NewMaxStorageGb;
        MaxUsers = ScheduledPlanChange.NewMaxUsers;
        FeaturesJson = ScheduledPlanChange.NewFeaturesJson;
        ScheduledPlanChange.Apply();
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new SubscriptionDowngradeAppliedEvent(Id, SubscriberId, PlanId));
    }
}
