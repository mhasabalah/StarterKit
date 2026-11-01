namespace Subscriptions.Domains.Subscriptions.Models;

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
    public string NewFeaturesJson { get; private set; } = "{}";
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
        Plan newPlan,
        string newFeaturesJson,
        PlanChangeType changeType,
        Guid scheduledBy,
        DateTimeOffset effectiveAt)
    {
        return new ScheduledPlanChange
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            CurrentPlanId = currentPlanId,
            NewPlanId = newPlan.Id,
            NewPlanName = newPlan.Name.En,
            NewPrice = newPlan.Price,
            NewBillingCycleInMonths = newPlan.BillingCycleInMonths,
            NewMaxItems = newPlan.MaxItems,
            NewMaxStorageGb = newPlan.MaxStorageGb,
            NewMaxUsers = newPlan.MaxUsers,
            NewFeaturesJson = newFeaturesJson,
            ChangeType = changeType,
            ScheduledBy = scheduledBy,
            ScheduledAt = DateTimeOffset.UtcNow,
            EffectiveAt = effectiveAt,
            Status = PlanChangeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Cancel()
    {
        Status = PlanChangeStatus.Cancelled;
    }

    public void Apply()
    {
        Status = PlanChangeStatus.Applied;
        AppliedAt = DateTimeOffset.UtcNow;
    }
}
