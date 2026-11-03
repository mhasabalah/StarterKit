namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// A plan change scheduled for the renewal boundary. A subscription has many over time (one-to-many);
/// at most one is Pending (filtered unique index on subscription_id WHERE status = 'Pending').
/// </summary>
public class ScheduledPlanChange : Aggregate<Guid>
{
    public Guid SubscriptionId { get; private set; }
    public Guid CurrentPlanId { get; private set; }
    public Guid NewPlanId { get; private set; }
    public string NewPlanName { get; private set; } = default!;
    public decimal NewPrice { get; private set; }
    public int NewBillingCycleInMonths { get; private set; }
    public int? NewMaxItems { get; private set; }
    public int? NewMaxStorageGb { get; private set; }
    public int? NewMaxUsers { get; private set; }
    public string NewFeaturesJson { get; private set; } = FeaturesJson.Empty;
    public PlanChangeType ChangeType { get; private set; }
    public Guid ScheduledBy { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset EffectiveAt { get; private set; }
    public PlanChangeStatus Status { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    private ScheduledPlanChange()
    {
    }

    public static ScheduledPlanChange Create(
        Guid subscriptionId,
        Guid currentPlanId,
        PlanSnapshot newPlan,
        PlanChangeType changeType,
        Guid scheduledBy,
        DateTimeOffset effectiveAt,
        DateTimeOffset now)
    {
        return new ScheduledPlanChange
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            CurrentPlanId = currentPlanId,
            NewPlanId = newPlan.PlanId,
            NewPlanName = newPlan.PlanName,
            NewPrice = newPlan.Price,
            NewBillingCycleInMonths = newPlan.BillingCycleInMonths,
            NewMaxItems = newPlan.MaxItems,
            NewMaxStorageGb = newPlan.MaxStorageGb,
            NewMaxUsers = newPlan.MaxUsers,
            NewFeaturesJson = newPlan.FeaturesJson,
            ChangeType = changeType,
            ScheduledBy = scheduledBy,
            ScheduledAt = now,
            EffectiveAt = effectiveAt,
            Status = PlanChangeStatus.Pending,
            CreatedAt = now
        };
    }

    /// <summary>Moves a pending change to a new effective date (admin extended the period it is tied to).</summary>
    public void Reschedule(DateTimeOffset effectiveAt)
    {
        if (Status != PlanChangeStatus.Pending)
            throw new ConflictException("Only a pending plan change can be rescheduled.");

        EffectiveAt = effectiveAt;
    }

    public PlanSnapshot ToSnapshot(string currency) => new(
        NewPlanId,
        NewPlanName,
        NewPrice,
        currency,
        NewBillingCycleInMonths,
        NewMaxItems,
        NewMaxStorageGb,
        NewMaxUsers,
        NewFeaturesJson);

    public void Cancel()
    {
        if (Status != PlanChangeStatus.Pending)
            throw new ConflictException($"Cannot cancel a {Status} plan change.");

        Status = PlanChangeStatus.Cancelled;
    }

    public void Apply(DateTimeOffset now)
    {
        if (Status != PlanChangeStatus.Pending)
            throw new ConflictException($"Cannot apply a {Status} plan change.");

        Status = PlanChangeStatus.Applied;
        AppliedAt = now;
    }
}
