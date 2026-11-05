using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Events;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Exceptions;

namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// A subscriber's subscription. Status changes go through <see cref="SubscriptionStateMachine"/>.
/// Billing periods: [<see cref="CurrentPeriodStart"/>, <see cref="RenewalDate"/>), anchored on <see cref="StartDate"/>.
/// Invoices are their own aggregate (<see cref="SubscriptionInvoice"/>), not loaded with the subscription.
/// </summary>
public class Subscription : Aggregate<Guid>
{
    private readonly List<SubscriptionPayment> _payments = [];
    private readonly List<ScheduledPlanChange> _scheduledPlanChanges = [];

    public Guid SubscriberId { get; private set; }

    /// <summary>
    /// Denormalized copy of the subscriber's display name (from <c>ISubscriberInfoProvider</c>), refreshed on create
    /// and on every approval, so admin search runs in SQL. Display/search only - never authoritative.
    /// </summary>
    public string? SubscriberDisplayName { get; private set; }

    /// <summary>Denormalized copy of the subscriber's email; see <see cref="SubscriberDisplayName"/>.</summary>
    public string? SubscriberEmail { get; private set; }

    /// <summary>
    /// End of a complimentary (comp) period granted by an admin; null otherwise. Cleared at the next renewal boundary.
    /// Comp periods earn no revenue and are excluded from MRR.
    /// </summary>
    public DateTimeOffset? ComplimentaryUntil { get; private set; }

    public Guid PlanId { get; private set; }
    public string PlanName { get; private set; } = default!;

    /// <summary>Price of one period (tax basis per <c>Subscriptions:Tax:PricesIncludeTax</c>).</summary>
    public decimal Price { get; private set; }

    public string Currency { get; private set; } = default!;
    public int BillingCycleInMonths { get; private set; }
    public int? MaxItems { get; private set; }
    public int? MaxStorageGb { get; private set; }
    public int? MaxUsers { get; private set; }
    public string FeaturesJson { get; private set; } = Plans.Models.FeaturesJson.Empty;
    public SubscriptionStatus Status { get; private set; }

    /// <summary>Activation date; also the billing anchor (renewal dates are computed from it).</summary>
    public DateTimeOffset? StartDate { get; private set; }

    /// <summary>Start of the current billing period (= the previous renewal date).</summary>
    public DateTimeOffset? CurrentPeriodStart { get; private set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? RenewalDate { get; private set; }

    public DateTimeOffset? EndDate { get; private set; }
    public DateTimeOffset? GracePeriodEndsAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public bool CancelledAtPeriodEnd { get; private set; }
    public DateTimeOffset? ScheduledCancellationDate { get; private set; }
    public CancellationReason? CancellationReason { get; private set; }
    public string? CancellationNote { get; private set; }

    /// <summary>Upgrade proration deferred to the next renewal (tax basis). Cleared when the renewal is billed.</summary>
    public decimal DeferredProrationAmount { get; private set; }

    /// <summary>When the subscriber's free trial began. Kept after the trial ends: it is the "one trial per subscriber" record.</summary>
    public DateTimeOffset? TrialStartedAt { get; private set; }

    /// <summary>End of the free trial (while Trialing it is also <see cref="RenewalDate"/>).</summary>
    public DateTimeOffset? TrialEndsAt { get; private set; }

    /// <summary>Set when the "trial ending" reminder was sent, so it goes out exactly once.</summary>
    public DateTimeOffset? TrialReminderSentAt { get; private set; }

    // Redeemed coupon (snapshot of its terms; see CouponTerms). Null CouponId = none.
    public Guid? CouponId { get; private set; }
    public string? CouponCode { get; private set; }
    public CouponDiscountType? CouponDiscountType { get; private set; }
    public decimal? CouponValue { get; private set; }

    /// <summary>Bills the coupon still discounts; null with a coupon = forever.</summary>
    public int? CouponPeriodsRemaining { get; private set; }

    /// <summary>The coupon that will discount the next bill, or null.</summary>
    public CouponTerms? ActiveCoupon =>
        CouponId is { } id && CouponDiscountType is { } type && CouponValue is { } value && CouponCode is { } code
            ? new CouponTerms(id, code, type, value, CouponPeriodsRemaining)
            : null;

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<SubscriptionPayment> Payments => _payments.AsReadOnly();
    public IReadOnlyList<ScheduledPlanChange> ScheduledPlanChanges => _scheduledPlanChanges.AsReadOnly();

    /// <summary>The pending scheduled change, if loaded (repositories load pending changes with the subscription).</summary>
    public ScheduledPlanChange? PendingPlanChange =>
        _scheduledPlanChanges.FirstOrDefault(x => x.Status == PlanChangeStatus.Pending);

    /// <summary>Start of the current period, falling back to one cycle before the renewal date for legacy rows.</summary>
    public DateTimeOffset? EffectivePeriodStart =>
        CurrentPeriodStart ??
        (RenewalDate is { } renewal ? BillingPeriod.PeriodStart(StartDate, renewal, BillingCycleInMonths) : null);

    private Subscription()
    {
    }

    /// <summary>Creates a PendingApproval subscription with its activation payment (<paramref name="amountDue"/>, gross).</summary>
    /// <param name="coupon">Coupon redeemed with the subscription; its first discounted bill is this activation bill.</param>
    /// <param name="couponDiscount">The discount (tax basis) already taken off <paramref name="amountDue"/>.</param>
    public static Subscription Create(
        Guid subscriberId,
        PlanSnapshot plan,
        decimal amountDue,
        DateTimeOffset now,
        CouponTerms? coupon = null,
        decimal couponDiscount = 0m)
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriberId = subscriberId,
            Status = SubscriptionStatus.PendingApproval,
            UpdatedAt = now
        };
        subscription.ApplyPlanSnapshot(plan);

        var activation = SubscriptionPayment.Create(
            subscription.Id, PaymentPurpose.Activation, amountDue, plan.Currency, now);
        subscription._payments.Add(activation);

        if (coupon is not null)
        {
            subscription.AttachCoupon(coupon);
            if (couponDiscount > 0m)
            {
                activation.RecordCoupon(coupon.Code, couponDiscount);
                subscription.ConsumeCouponPeriod();
            }
        }

        subscription.AddDomainEvent(new SubscriptionCreatedEvent(
            subscription.Id,
            subscription.SubscriberId,
            subscription.PlanId,
            subscription.PlanName));

        return subscription;
    }

    /// <summary>
    /// Starts a free trial: Trialing with full access and NO payment. The trial end is also the renewal date, so the
    /// renewal orchestrator picks it up at the boundary and bills the first paid period.
    /// </summary>
    public static Subscription StartTrial(Guid subscriberId, PlanSnapshot plan, int trialDays, DateTimeOffset now)
    {
        if (trialDays < 1)
            throw new ConflictException("This plan does not offer a free trial.");

        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriberId = subscriberId,
            Status = SubscriptionStatus.Trialing,
            StartDate = now,
            CurrentPeriodStart = now,
            TrialStartedAt = now,
            TrialEndsAt = now.AddDays(trialDays),
            UpdatedAt = now
        };
        subscription.RenewalDate = subscription.TrialEndsAt;
        subscription.ApplyPlanSnapshot(plan);

        subscription.AddDomainEvent(new SubscriptionCreatedEvent(
            subscription.Id, subscription.SubscriberId, subscription.PlanId, subscription.PlanName));
        subscription.AddDomainEvent(new SubscriptionActivatedEvent(
            subscription.Id, subscription.SubscriberId, now, subscription.RenewalDate.Value));
        subscription.AddIntegrationEvent(new SubscriptionActivatedIntegrationEvent
        {
            SubscriptionId = subscription.Id,
            SubscriberId = subscription.SubscriberId
        });

        return subscription;
    }

    /// <summary>
    /// The trial elapsed: the first paid period is billed (a Renewal payment for <paramref name="charge"/>) and the
    /// subscription goes PastDue with <paramref name="gracePeriodDays"/> to pay. Paying converts it to Active (the
    /// normal renewal path); otherwise it expires. The billing anchor restarts at the trial end.
    /// </summary>
    public SubscriptionPayment EndTrial(RenewalCharge charge, int gracePeriodDays, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Trialing)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.PastDue);

        StartDate = RenewalDate ?? now;
        CurrentPeriodStart = StartDate;
        return BillPeriod(charge, gracePeriodDays, now);
    }

    /// <summary>Marks the trial-ending reminder as sent (exactly-once guard for the reminder job).</summary>
    public void MarkTrialReminderSent(DateTimeOffset now)
    {
        TrialReminderSentAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Attaches a redeemed coupon to a running subscription (Trialing, Active or PastDue); it discounts the next bills.
    /// One coupon at a time.
    /// </summary>
    public void RedeemCoupon(CouponTerms coupon, DateTimeOffset now)
    {
        if (Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue))
            throw new ConflictException($"A coupon cannot be applied to a {Status} subscription.");

        if (CouponId is not null)
            throw new ConflictException("The subscription already has a coupon applied.");

        AttachCoupon(coupon);
        UpdatedAt = now;
    }

    private void AttachCoupon(CouponTerms coupon)
    {
        CouponId = coupon.CouponId;
        CouponCode = coupon.Code;
        CouponDiscountType = coupon.Type;
        CouponValue = coupon.Value;
        CouponPeriodsRemaining = coupon.PeriodsRemaining;
    }

    /// <summary>One discounted bill was issued: count it, and drop the coupon after its last bill.</summary>
    private void ConsumeCouponPeriod()
    {
        if (CouponId is null || CouponPeriodsRemaining is null)
            return; // no coupon, or forever

        CouponPeriodsRemaining--;
        if (CouponPeriodsRemaining <= 0)
            ClearCoupon();
    }

    private void ClearCoupon()
    {
        CouponId = null;
        CouponCode = null;
        CouponDiscountType = null;
        CouponValue = null;
        CouponPeriodsRemaining = null;
    }

    /// <summary>Refreshes the denormalized subscriber name/email (null values keep the previous snapshot).</summary>
    public void UpdateSubscriberSnapshot(string? displayName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            SubscriberDisplayName = Truncate(displayName.Trim(), 200);

        if (!string.IsNullOrWhiteSpace(email))
            SubscriberEmail = Truncate(email.Trim(), 256);
    }

    /// <summary>
    /// Records billing activity on the subscription (e.g. a partial manual receipt) without a status change, so the
    /// row is updated: the optimistic-concurrency check applies and concurrent admin actions serialize.
    /// </summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>The subscription's own plan terms as a snapshot (used when the plan itself is gone).</summary>
    public PlanSnapshot CurrentPlanSnapshot() =>
        new(PlanId, PlanName, Price, Currency, BillingCycleInMonths, MaxItems, MaxStorageGb, MaxUsers, FeaturesJson);

    /// <summary>
    /// Admin grants extra days. Active: the renewal date (and a scheduled cancellation / pending downgrade tied to
    /// it) moves by <paramref name="days"/>. PastDue: the grace period is extended instead (the period was already
    /// billed). Other statuses: 409.
    /// </summary>
    public void Extend(int days, DateTimeOffset now)
    {
        if (days is < 1 or > MaxExtensionDays)
            throw new BadRequestException($"Days must be between 1 and {MaxExtensionDays}.");

        switch (Status)
        {
            case SubscriptionStatus.Active when RenewalDate is { } renewal:
                RenewalDate = renewal.AddDays(days);
                if (CancelledAtPeriodEnd)
                    ScheduledCancellationDate = RenewalDate;
                PendingPlanChange?.Reschedule(RenewalDate.Value);
                if (ComplimentaryUntil is not null)
                    ComplimentaryUntil = RenewalDate;
                break;
            case SubscriptionStatus.PastDue:
                GracePeriodEndsAt = (GracePeriodEndsAt ?? now).AddDays(days);
                break;
            default:
                throw new ConflictException($"A {Status} subscription cannot be extended.");
        }

        UpdatedAt = now;
    }

    public const int MaxExtensionDays = 366;
    public const int MaxComplimentaryMonths = 36;

    /// <summary>
    /// Admin reopens a Cancelled/Expired subscription that ended within <paramref name="windowDays"/>: back to
    /// PendingApproval with a NEW activation payment for <paramref name="amountDue"/> (gross) at
    /// <paramref name="periodPrice"/>. The normal approval (proof or manual payment) then activates it with a fresh
    /// billing anchor.
    /// </summary>
    public SubscriptionPayment Reactivate(decimal periodPrice, decimal amountDue, int windowDays, DateTimeOffset now)
    {
        EnsureReactivatable(windowDays, now);
        ReopenAsPending(now);
        Price = periodPrice;

        var payment = SubscriptionPayment.Create(Id, PaymentPurpose.Activation, amountDue, Currency, now);
        _payments.Add(payment);
        return payment;
    }

    /// <summary>
    /// Complimentary (zero-price) activation on <paramref name="plan"/> for <paramref name="months"/> months:
    /// PendingApproval → Active, or (reactivation) Cancelled/Expired within <paramref name="windowDays"/> → Active.
    /// Adds a Paid zero-amount activation payment (returned) so the normal invoice/ledger path applies. Callers waive
    /// any payment still owed. The next renewal is billed normally at the plan price.
    /// </summary>
    public SubscriptionPayment ActivateComplimentary(
        PlanSnapshot plan,
        int months,
        Guid adminId,
        int windowDays,
        DateTimeOffset now)
    {
        if (months is < 1 or > MaxComplimentaryMonths)
            throw new BadRequestException($"Months must be between 1 and {MaxComplimentaryMonths}.");

        if (SubscriptionStateMachine.CanReactivate(Status))
        {
            EnsureReactivatable(windowDays, now);
            ReopenAsPending(now);
        }
        else if (Status != SubscriptionStatus.PendingApproval)
        {
            throw new ConflictException(
                $"A {Status} subscription cannot be comped; only pending, cancelled or expired ones (extend an active one).");
        }

        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.Active);
        ApplyPlanSnapshot(plan);

        Status = SubscriptionStatus.Active;
        StartDate = now;
        CurrentPeriodStart = now;
        RenewalDate = now.AddMonths(months);
        ComplimentaryUntil = RenewalDate;
        UpdatedAt = now;

        var payment = SubscriptionPayment.CreateComplimentary(Id, Currency, adminId, now);
        _payments.Add(payment);

        AddDomainEvent(new SubscriptionActivatedEvent(Id, SubscriberId, StartDate.Value, RenewalDate.Value));
        AddIntegrationEvent(new SubscriptionActivatedIntegrationEvent { SubscriptionId = Id, SubscriberId = SubscriberId });
        return payment;
    }

    private void EnsureReactivatable(int windowDays, DateTimeOffset now)
    {
        if (!SubscriptionStateMachine.CanReactivate(Status))
            throw new ConflictException($"A {Status} subscription cannot be reactivated; only cancelled or expired ones.");

        var endedAt = EndDate ?? CancelledAt ?? UpdatedAt;
        if (endedAt.AddDays(windowDays) < now)
            throw new ConflictException(
                $"The subscription ended more than {windowDays} days ago; create a new subscription instead.");
    }

    /// <summary>Cancelled/Expired → PendingApproval with every period/cancellation field reset (fresh anchor).</summary>
    private void ReopenAsPending(DateTimeOffset now)
    {
        Status = SubscriptionStatus.PendingApproval;
        StartDate = null;
        CurrentPeriodStart = null;
        RenewalDate = null;
        EndDate = null;
        GracePeriodEndsAt = null;
        CancelledAt = null;
        CancelledAtPeriodEnd = false;
        ScheduledCancellationDate = null;
        CancellationReason = null;
        CancellationNote = null;
        DeferredProrationAmount = 0m;
        ComplimentaryUntil = null;
        ClearCoupon(); // a reopened subscription starts clean; the trial record (TrialStartedAt) stays
        UpdatedAt = now;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public SubscriptionPayment ActivationPayment =>
        _payments.Where(x => x.Purpose == PaymentPurpose.Activation).OrderBy(x => x.CreatedAt).First();

    public void Activate(DateTimeOffset now)
    {
        // Activation and renewal both lead to Active; each also pins its own source status.
        if (Status != SubscriptionStatus.PendingApproval)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.Active);

        Status = SubscriptionStatus.Active;
        StartDate = now;
        CurrentPeriodStart = now;
        RenewalDate = BillingPeriod.FirstRenewal(now, BillingCycleInMonths);
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionActivatedEvent(Id, SubscriberId, StartDate.Value, RenewalDate.Value));
        AddIntegrationEvent(new SubscriptionActivatedIntegrationEvent { SubscriptionId = Id, SubscriberId = SubscriberId });
    }

    public void Reject(string? reason, DateTimeOffset now)
    {
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.Rejected);

        Status = SubscriptionStatus.Rejected;
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionRejectedEvent(Id, SubscriberId, reason));
    }

    public void CancelImmediately(Guid actorId, CancellationReason? reason, string? note, DateTimeOffset now)
    {
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.Cancelled);

        PendingPlanChange?.Cancel();

        CancellationReason = reason;
        CancellationNote = note;
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = now;
        EndDate = now;
        CancelledAtPeriodEnd = false;
        ScheduledCancellationDate = null;
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionCancelledEvent(Id, SubscriberId, now, actorId, reason, IsScheduled: false));
    }

    public void ScheduleCancellation(Guid actorId, CancellationReason? reason, string? note, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Cancelled);

        if (CancelledAtPeriodEnd)
            throw new ConflictException("A cancellation is already scheduled for end of period.");

        CancellationReason = reason;
        CancellationNote = note;
        CancelledAt = now;
        CancelledAtPeriodEnd = true;
        ScheduledCancellationDate = RenewalDate ?? now;
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionCancellationScheduledEvent(Id, SubscriberId, actorId, ScheduledCancellationDate.Value, reason));
    }

    /// <summary>Applies an end-of-period cancellation at the boundary. Access ended at the scheduled date.</summary>
    public void ApplyScheduledCancellation(Guid systemActorId, DateTimeOffset now)
    {
        if (!CancelledAtPeriodEnd || Status != SubscriptionStatus.Active)
            throw new ConflictException("No scheduled cancellation to apply.");

        if (ScheduledCancellationDate is { } date && date > now)
            throw new ConflictException("The scheduled cancellation date has not been reached.");

        PendingPlanChange?.Cancel();

        Status = SubscriptionStatus.Cancelled;
        EndDate = ScheduledCancellationDate ?? now;
        CancelledAtPeriodEnd = false;
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionCancelledEvent(Id, SubscriberId, EndDate.Value, systemActorId, CancellationReason, IsScheduled: true));
    }

    /// <summary>
    /// Renewal boundary: bills the next period. The price snapshot becomes the period price, any deferred proration is
    /// included in this bill and cleared, and a Renewal payment for <see cref="RenewalCharge.AmountDue"/> is created.
    /// </summary>
    public SubscriptionPayment MarkPastDue(RenewalCharge charge, int gracePeriodDays, DateTimeOffset now)
    {
        // A trial ends through EndTrial; this is the Active renewal boundary.
        if (Status != SubscriptionStatus.Active)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.PastDue);
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.PastDue);
        return BillPeriod(charge, gracePeriodDays, now);
    }

    private SubscriptionPayment BillPeriod(RenewalCharge charge, int gracePeriodDays, DateTimeOffset now)
    {
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.PastDue);

        Price = charge.PeriodPrice;
        DeferredProrationAmount = 0m;
        ComplimentaryUntil = null;
        Status = SubscriptionStatus.PastDue;
        GracePeriodEndsAt = now.AddDays(gracePeriodDays);
        UpdatedAt = now;

        var payment = SubscriptionPayment.Create(
            Id, PaymentPurpose.Renewal, charge.AmountDue, Currency, now, includedProrationAmount: charge.DeferredProration);
        if (charge.CouponDiscount > 0m && charge.CouponCode is { } couponCode)
        {
            payment.RecordCoupon(couponCode, charge.CouponDiscount);
            ConsumeCouponPeriod();
        }

        _payments.Add(payment);
        AddDomainEvent(new SubscriptionPastDueEvent(Id, SubscriberId, GracePeriodEndsAt.Value, payment.Id));

        return payment;
    }

    /// <summary>
    /// Renewal paid: the new period starts at the previous renewal date (not "now"), so a late payment or a slow
    /// admin never shifts the billing cycle.
    /// </summary>
    public void Renew(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.PastDue)
            throw new InvalidSubscriptionTransitionException(Status, SubscriptionStatus.Active);
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.Active);

        var previousRenewal = RenewalDate ?? now;
        Status = SubscriptionStatus.Active;
        CurrentPeriodStart = previousRenewal;
        RenewalDate = BillingPeriod.NextRenewal(StartDate ?? previousRenewal, previousRenewal, BillingCycleInMonths);
        GracePeriodEndsAt = null;
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionRenewedEvent(Id, SubscriberId, RenewalDate.Value));
    }

    public void Expire(DateTimeOffset now)
    {
        SubscriptionStateMachine.EnsureCanTransition(Status, SubscriptionStatus.Expired);

        Status = SubscriptionStatus.Expired;
        EndDate = now;
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionExpiredEvent(Id, SubscriberId));
        AddIntegrationEvent(new SubscriptionExpiredIntegrationEvent { SubscriptionId = Id, SubscriberId = SubscriberId });
    }

    /// <summary>
    /// Immediate upgrade. Cancels any pending scheduled downgrade. A deferred proration is accumulated for the next
    /// renewal; otherwise a Proration payment for <paramref name="amountDue"/> (gross) is created and returned.
    /// </summary>
    public SubscriptionPayment? UpgradePlan(
        PlanSnapshot newPlan,
        decimal proratedAmount,
        bool isDeferred,
        decimal amountDue,
        DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active)
            throw new ConflictException("Only active subscriptions can be upgraded.");

        if (CancelledAtPeriodEnd)
            throw new ConflictException("The subscription is scheduled for cancellation; it cannot be upgraded.");

        EnsureCompatible(newPlan);

        if (newPlan.Price <= Price)
            throw new ConflictException("New plan price must be higher than the current plan price.");

        if (_payments.Any(x => x.Purpose == PaymentPurpose.Proration && x.IsAwaitingPayment))
            throw new ConflictException("A previous upgrade is still awaiting payment.");

        if (PendingPlanChange is { } pending)
        {
            pending.Cancel();
            AddDomainEvent(new SubscriptionDowngradeCancelledEvent(Id, SubscriberId));
        }

        var oldPlanId = PlanId;
        ApplyPlanSnapshot(newPlan);
        UpdatedAt = now;

        SubscriptionPayment? payment = null;
        if (isDeferred)
        {
            DeferredProrationAmount += proratedAmount;
        }
        else if (amountDue > 0m)
        {
            payment = SubscriptionPayment.Create(
                Id, PaymentPurpose.Proration, amountDue, Currency, now, originalPlanId: oldPlanId);
            _payments.Add(payment);
        }

        AddDomainEvent(new SubscriptionUpgradedEvent(Id, SubscriberId, oldPlanId, PlanId, proratedAmount, isDeferred));
        return payment;
    }

    /// <summary>Reverts an unpaid upgrade to the original plan terms.</summary>
    public void RevertUpgrade(PlanSnapshot originalPlan, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active)
            throw new ConflictException("Only active subscriptions can have an upgrade reverted.");

        ApplyPlanSnapshot(originalPlan);
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionUpgradeRevertedEvent(Id, SubscriberId, originalPlan.PlanId));
    }

    public ScheduledPlanChange ScheduleDowngrade(PlanSnapshot newPlan, Guid scheduledBy, DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active)
            throw new ConflictException("Only active subscriptions can have a downgrade scheduled.");

        if (CancelledAtPeriodEnd)
            throw new ConflictException("The subscription is scheduled for cancellation; a downgrade cannot be scheduled.");

        if (PendingPlanChange is not null)
            throw new ConflictException("A pending scheduled downgrade already exists. Cancel it first.");

        EnsureCompatible(newPlan);

        if (newPlan.Price >= Price)
            throw new ConflictException("New plan price must be lower than the current plan price for a downgrade.");

        var effectiveAt = RenewalDate ?? BillingPeriod.FirstRenewal(now, BillingCycleInMonths);
        var change = ScheduledPlanChange.Create(Id, PlanId, newPlan, PlanChangeType.Downgrade, scheduledBy, effectiveAt, now);
        _scheduledPlanChanges.Add(change);

        UpdatedAt = now;
        AddDomainEvent(new SubscriptionDowngradeScheduledEvent(Id, SubscriberId, newPlan.PlanId, effectiveAt));
        return change;
    }

    public void CancelScheduledDowngrade(DateTimeOffset now)
    {
        var pending = PendingPlanChange
            ?? throw new ConflictException("No pending scheduled downgrade exists.");

        pending.Cancel();
        UpdatedAt = now;
        AddDomainEvent(new SubscriptionDowngradeCancelledEvent(Id, SubscriberId));
    }

    /// <summary>Applies the pending downgrade at the renewal boundary. Only for Active subscriptions.</summary>
    public void ApplyScheduledDowngrade(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.Active)
            throw new ConflictException("A scheduled downgrade can only be applied to an active subscription.");

        var pending = PendingPlanChange;
        if (pending is null || pending.EffectiveAt > now)
            throw new ConflictException("No applicable scheduled downgrade found.");

        ApplyPlanSnapshot(pending.ToSnapshot(Currency));
        pending.Apply(now);
        UpdatedAt = now;

        AddDomainEvent(new SubscriptionDowngradeAppliedEvent(Id, SubscriberId, PlanId));
    }

    private void EnsureCompatible(PlanSnapshot plan)
    {
        if (!string.Equals(plan.Currency, Currency, StringComparison.OrdinalIgnoreCase) ||
            plan.BillingCycleInMonths != BillingCycleInMonths)
        {
            throw new PlanChangeNotSupportedException();
        }
    }

    private void ApplyPlanSnapshot(PlanSnapshot plan)
    {
        PlanId = plan.PlanId;
        PlanName = plan.PlanName;
        Price = plan.Price;
        Currency = plan.Currency;
        BillingCycleInMonths = plan.BillingCycleInMonths;
        MaxItems = plan.MaxItems;
        MaxStorageGb = plan.MaxStorageGb;
        MaxUsers = plan.MaxUsers;
        FeaturesJson = plan.FeaturesJson;
    }
}
