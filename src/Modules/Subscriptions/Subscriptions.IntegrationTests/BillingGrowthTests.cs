using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Contracts.Enums;
using Shared.Contracts.Localization;
using Shared.Core.Entitlements;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Commands.Coupons;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Data;
using Subscriptions.Domains.Coupons.Exceptions;
using Subscriptions.Domains.Metering;
using Subscriptions.Domains.Plans.Models;
using Subscriptions.Domains.Subscriptions.Models;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class BillingGrowthTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    private IRenewalOrchestrator Orchestrator => fx.Services.GetRequiredService<IRenewalOrchestrator>();

    private Task<Plan> PlanAsync(string name, decimal price, int trialDays = 0, params (string Key, long? Limit)[] features) =>
        fx.WithDb(async db =>
        {
            var plan = Plan.Create(LocalizedText.Create(name, name), null, price, "SAR", 1, 10, 5, 3, true, trialDays: trialDays);
            foreach (var (key, limit) in features)
                plan.AddFeature(key, limit);
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
            return plan;
        });

    private async Task<Guid> CouponAsync(
        string code, CouponDiscountType type = CouponDiscountType.Percent, decimal value = 20m,
        CouponDuration duration = CouponDuration.Once, int? periods = null, int? max = null,
        DateTimeOffset? expires = null, List<Guid>? plans = null) =>
        await fx.SendAsAdmin(new CreateCouponCommand(
            code, null, type, value, type == CouponDiscountType.FixedAmount ? "SAR" : null,
            duration, periods, max, expires, plans));

    // ---------------------------------------------------------------- trials

    [Fact]
    public async Task Trial_starts_without_payment_converts_into_a_renewal_bill_and_pays_into_active()
    {
        var plan = await PlanAsync("Trial-T1", 100m, trialDays: 14);
        var subscriberId = Guid.NewGuid();

        var started = await fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id));

        started.Status.Should().Be("Trialing");
        (await _scenario.PaymentsAsync(started.SubscriptionId)).Should().BeEmpty();

        var outcome = await Orchestrator.ProcessBoundaryAsync(started.SubscriptionId, started.TrialEndsAt.AddMinutes(1));

        outcome.Should().Be(RenewalBoundaryOutcome.TrialEnded);
        var pastDue = await _scenario.LoadAsync(started.SubscriptionId);
        pastDue.Status.Should().Be(SubscriptionStatus.PastDue);
        pastDue.GracePeriodEndsAt.Should().BeCloseTo(started.TrialEndsAt.AddMinutes(1).AddDays(3), TimeSpan.FromSeconds(2));
        var payment = (await _scenario.PaymentsAsync(started.SubscriptionId)).Single();
        payment.Purpose.Should().Be(PaymentPurpose.Renewal);
        payment.Amount.Should().Be(100m);

        await _scenario.UploadRenewalProofAsync(subscriberId, started.SubscriptionId);
        await fx.SendAsAdmin(new ApproveRenewalCommand(started.SubscriptionId, TestIds.Admin, payment.Id, pastDue.Version));

        var active = await _scenario.LoadAsync(started.SubscriptionId);
        active.Status.Should().Be(SubscriptionStatus.Active);
        active.CurrentPeriodStart.Should().BeCloseTo(started.TrialEndsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Unpaid_trial_expires_after_the_post_trial_grace()
    {
        var plan = await PlanAsync("Trial-T2", 100m, trialDays: 7);
        var subscriberId = Guid.NewGuid();
        var started = await fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id));
        var ended = started.TrialEndsAt.AddMinutes(1);
        await Orchestrator.ProcessBoundaryAsync(started.SubscriptionId, ended);

        var expired = await Orchestrator.ProcessExpiriesAsync(ended.AddDays(3).AddMinutes(1));

        expired.Should().BeGreaterThanOrEqualTo(1);
        (await _scenario.LoadAsync(started.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Expired);
    }

    [Fact]
    public async Task Only_one_trial_per_subscriber_even_after_it_ended()
    {
        var plan = await PlanAsync("Trial-T3", 100m, trialDays: 7);
        var subscriberId = Guid.NewGuid();
        var first = await fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id));

        // While it runs: the open subscription blocks a second one.
        await FluentActions.Awaiting(() => fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id)))
            .Should().ThrowAsync<ConflictException>();

        // After cancelling it, the trial is still "used".
        await fx.SendAs(subscriberId, new CancelSubscriptionCommand(first.SubscriptionId, subscriberId, false, "bye"));
        (await _scenario.LoadAsync(first.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Cancelled);

        var again = await FluentActions.Awaiting(() => fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id)))
            .Should().ThrowAsync<ConflictException>();
        again.Which.ErrorCode.Should().Be("trial_already_used");
    }

    [Fact]
    public async Task Trial_requires_a_plan_that_offers_one()
    {
        var plan = await PlanAsync("NoTrial-T4", 100m);
        var subscriberId = Guid.NewGuid();

        var act = await FluentActions.Awaiting(() => fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id)))
            .Should().ThrowAsync<ConflictException>();

        act.Which.ErrorCode.Should().Be("plan_has_no_trial");
    }

    [Fact]
    public async Task Trial_reminder_is_sent_once_inside_the_window_and_never_before_it()
    {
        var plan = await PlanAsync("Trial-T5", 100m, trialDays: 10);
        var subscriberId = Guid.NewGuid();
        var started = await fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id));
        var processor = fx.Services.GetRequiredService<ITrialReminderProcessor>();
        fx.Notifications.Messages.Clear();

        (await processor.ProcessAsync(started.TrialEndsAt.AddDays(-8))).Should().Be(0, "outside the 3 day window");
        (await processor.ProcessAsync(started.TrialEndsAt.AddDays(-2))).Should().BeGreaterThanOrEqualTo(1);
        var sentAfterFirst = fx.Notifications.Messages.Count(m => m.UserId == subscriberId && m.Email?.Template == EmailTemplate.TrialEnding);
        await processor.ProcessAsync(started.TrialEndsAt.AddDays(-1));

        sentAfterFirst.Should().Be(1);
        fx.Notifications.Messages.Count(m => m.UserId == subscriberId && m.Email?.Template == EmailTemplate.TrialEnding)
            .Should().Be(1, "a second run never re-sends");
        (await _scenario.LoadAsync(started.SubscriptionId)).TrialReminderSentAt.Should().NotBeNull();
    }

    // ---------------------------------------------------------------- coupons

    [Fact]
    public async Task Coupon_at_creation_discounts_the_activation_bill_and_is_snapshotted_on_the_invoice()
    {
        var plan = await PlanAsync("Coupon-C1", 100m);
        var code = "C1-" + Guid.NewGuid().ToString("N")[..8];
        await CouponAsync(code, value: 20m);
        var subscriberId = Guid.NewGuid();

        var created = await fx.SendAs(subscriberId, new CreateSubscriptionCommand(subscriberId, plan.Id, code.ToLowerInvariant()));

        created.AmountDue.Should().Be(80m);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        await fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId));
        var invoice = (await _scenario.InvoicesAsync(created.SubscriptionId)).Single();
        invoice.TotalAmount.Should().Be(80m);
        invoice.CouponCode.Should().Be(code.ToUpperInvariant());
        invoice.CouponDiscountAmount.Should().Be(20m);
        invoice.Description.Should().Contain("coupon");
    }

    [Fact]
    public async Task Redeemed_coupon_discounts_the_renewal_for_its_duration_only()
    {
        var plan = await PlanAsync("Coupon-C2", 100m);
        var code = "C2-" + Guid.NewGuid().ToString("N")[..8];
        await CouponAsync(code, CouponDiscountType.FixedAmount, 30m, CouponDuration.Once);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);

        var redeemed = await fx.SendAs(subscriberId, new RedeemCouponCommand(subscriberId, code));
        redeemed.PeriodsRemaining.Should().Be(1);

        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));
        var first = (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Purpose == PaymentPurpose.Renewal);
        first.Amount.Should().Be(70m);
        first.CouponCode.Should().Be(code.ToUpperInvariant());

        await _scenario.UploadRenewalProofAsync(subscriberId, subscriptionId);
        var pastDue = await _scenario.LoadAsync(subscriptionId);
        await fx.SendAsAdmin(new ApproveRenewalCommand(subscriptionId, TestIds.Admin, first.Id, pastDue.Version));
        var renewed = await _scenario.LoadAsync(subscriptionId);
        renewed.CouponId.Should().BeNull("a Once coupon is spent after one bill");

        await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewed.RenewalDate!.Value.AddHours(1));
        (await _scenario.PaymentsAsync(subscriptionId)).Last(x => x.Purpose == PaymentPurpose.Renewal).Amount.Should().Be(100m);
    }

    [Fact]
    public async Task Coupon_cannot_be_redeemed_twice_by_the_same_subscriber()
    {
        var plan = await PlanAsync("Coupon-C3", 100m);
        var code = "C3-" + Guid.NewGuid().ToString("N")[..8];
        await CouponAsync(code, duration: CouponDuration.Forever);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAs(subscriberId, new RedeemCouponCommand(subscriberId, code));

        await FluentActions.Awaiting(() => fx.SendAs(subscriberId, new RedeemCouponCommand(subscriberId, code)))
            .Should().ThrowAsync<CouponAlreadyRedeemedException>();
        (await _scenario.LoadAsync(subscriptionId)).CouponPeriodsRemaining.Should().BeNull("forever");
    }

    [Fact]
    public async Task Coupon_rules_expiry_plan_restriction_inactive_unknown_and_pending_subscription()
    {
        var plan = await PlanAsync("Coupon-C4", 100m);
        var other = await PlanAsync("Coupon-C4-other", 100m);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var expired = await CouponAsync("EXP-" + suffix, expires: DateTimeOffset.UtcNow.AddMinutes(-5));
        await CouponAsync("PLAN-" + suffix, plans: [other.Id]);
        var inactive = await CouponAsync("OFF-" + suffix);
        var coupon = await fx.WithDb(db => db.Coupons.AsNoTracking().SingleAsync(x => x.Id == inactive));
        await fx.SendAsAdmin(new UpdateCouponCommand(inactive, null, null, null, null, false, coupon.Version));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);

        async Task<string?> CodeOf(string code) =>
            (await FluentActions.Awaiting(() => fx.SendAs(subscriberId, new RedeemCouponCommand(subscriberId, code)))
                .Should().ThrowAsync<CustomException>()).Which.ErrorCode;

        (await CodeOf("EXP-" + suffix)).Should().Be(InvalidCouponException.Expired);
        (await CodeOf("PLAN-" + suffix)).Should().Be(InvalidCouponException.PlanNotAllowed);
        (await CodeOf("OFF-" + suffix)).Should().Be(InvalidCouponException.Inactive);
        await FluentActions.Awaiting(() => fx.SendAs(subscriberId, new RedeemCouponCommand(subscriberId, "NOPE-" + suffix)))
            .Should().ThrowAsync<CouponNotFoundException>();

        var pending = Guid.NewGuid();
        await _scenario.CreateAsync(pending, plan);
        await FluentActions.Awaiting(() => fx.SendAs(pending, new RedeemCouponCommand(pending, "EXP-" + suffix)))
            .Should().ThrowAsync<ConflictException>();
        expired.Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_last_redemption_goes_to_exactly_one_of_two_concurrent_subscribers()
    {
        var plan = await PlanAsync("Coupon-C5", 100m);
        var code = "LAST-" + Guid.NewGuid().ToString("N")[..8];
        var couponId = await CouponAsync(code, max: 1);
        var a = await _scenario.ActiveAsync(plan);
        var b = await _scenario.ActiveAsync(plan);

        var results = await Task.WhenAll(
            Try(() => fx.SendAs(a.SubscriberId, new RedeemCouponCommand(a.SubscriberId, code))),
            Try(() => fx.SendAs(b.SubscriberId, new RedeemCouponCommand(b.SubscriberId, code))));

        results.Count(ok => ok).Should().Be(1);
        (await fx.WithDb(db => db.Coupons.AsNoTracking().SingleAsync(x => x.Id == couponId))).RedemptionCount.Should().Be(1);
        (await fx.WithDb(db => db.CouponRedemptions.CountAsync(x => x.CouponId == couponId))).Should().Be(1);

        static async Task<bool> Try(Func<Task<RedeemCouponResult>> act)
        {
            try
            { await act(); return true; }
            catch (Exception ex) when (ex is ConflictException or ConcurrencyConflictException or InvalidCouponException) { return false; }
        }
    }

    [Fact]
    public async Task Duplicate_codes_are_rejected_and_redeemed_coupons_can_only_be_deactivated()
    {
        var plan = await PlanAsync("Coupon-C6", 100m);
        var code = "DUP-" + Guid.NewGuid().ToString("N")[..8];
        var id = await CouponAsync(code);

        await FluentActions.Awaiting(() => CouponAsync(code.ToLowerInvariant())).Should().ThrowAsync<CouponAlreadyExistsException>();

        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAs(subscriberId, new RedeemCouponCommand(subscriberId, code));
        await FluentActions.Awaiting(() => fx.SendAsAdmin(new DeleteCouponCommand(id))).Should().ThrowAsync<ConflictException>();

        var unused = await CouponAsync("DEL-" + Guid.NewGuid().ToString("N")[..8]);
        await fx.SendAsAdmin(new DeleteCouponCommand(unused));
        (await fx.WithDb(db => db.Coupons.AnyAsync(x => x.Id == unused))).Should().BeFalse();
    }

    [Fact]
    public async Task Stale_coupon_update_is_rejected()
    {
        var id = await CouponAsync("VER-" + Guid.NewGuid().ToString("N")[..8]);
        var current = await fx.WithDb(db => db.Coupons.AsNoTracking().SingleAsync(x => x.Id == id));
        await fx.SendAsAdmin(new UpdateCouponCommand(id, "first", null, null, null, true, current.Version));

        await FluentActions.Awaiting(() => fx.SendAsAdmin(new UpdateCouponCommand(id, "stale", null, null, null, true, current.Version)))
            .Should().ThrowAsync<Exception>();
    }

    // ---------------------------------------------------------------- dunning

    [Fact]
    public async Task Dunning_sends_each_step_once_with_a_fresh_checkout_and_a_final_notice()
    {
        var plan = await PlanAsync("Dunning-D1", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        var pastDueAt = renewal.AddHours(1);
        await Orchestrator.ProcessBoundaryAsync(subscriptionId, pastDueAt);
        var payment = (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Purpose == PaymentPurpose.Renewal);
        var processor = fx.Services.GetRequiredService<IDunningProcessor>();
        fx.Notifications.Messages.Clear();

        int Sent() => fx.Notifications.Messages.Count(m => m.UserId == subscriberId && m.Email?.Template == EmailTemplate.SubscriptionDunning);
        // The payment row's CreatedAt is wall-clock "now" (real time), so schedule relative to it.
        var t0 = payment.CreatedAt;
        // Pin the wall clock the job would see: the processor takes `now` explicitly.
        (await processor.ProcessAsync(t0.AddHours(12))).Should().Be(0);
        (await processor.ProcessAsync(t0.AddDays(1).AddMinutes(1))).Should().BeGreaterThanOrEqualTo(1);
        var afterDay1 = Sent();
        await processor.ProcessAsync(t0.AddDays(1).AddHours(2));
        Sent().Should().Be(afterDay1, "a step never re-sends");

        await processor.ProcessAsync(t0.AddDays(3).AddMinutes(1));
        Sent().Should().Be(afterDay1 + 1);

        // The job was down until the final window: day-5 and final are due together; only the final goes out.
        await processor.ProcessAsync(t0.AddDays(6).AddMinutes(1));
        Sent().Should().Be(afterDay1 + 2);
        var steps = await fx.WithDb(db => db.DunningNotices.AsNoTracking().Where(x => x.PaymentId == payment.Id).ToListAsync());
        steps.Should().Contain(x => x.Step == "final" && x.Sent);
        steps.Should().Contain(x => x.Step == "day-5" && !x.Sent, "superseded by the final notice");
        fx.Notifications.Messages.Where(m => m.Email?.Template == EmailTemplate.SubscriptionDunning && m.UserId == subscriberId)
            .Should().OnlyContain(m => m.Email!.Model.ContainsKey("PaymentUrl"));
    }

    [Fact]
    public async Task Dunning_skips_a_renewal_whose_proof_is_under_review()
    {
        var plan = await PlanAsync("Dunning-D2", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));
        await _scenario.UploadRenewalProofAsync(subscriberId, subscriptionId);
        var payment = (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Purpose == PaymentPurpose.Renewal);
        fx.Notifications.Messages.Clear();

        await fx.Services.GetRequiredService<IDunningProcessor>().ProcessAsync(payment.CreatedAt.AddDays(4));

        fx.Notifications.Messages.Count(m => m.UserId == subscriberId && m.Email?.Template == EmailTemplate.SubscriptionDunning)
            .Should().Be(0);
    }

    // ---------------------------------------------------------------- metering

    private async Task<T> AsSubscriberAsync<T>(Guid subscriberId, Func<IServiceProvider, Task<T>> act, bool enforce = true)
    {
        fx.Http.SetUser(subscriberId, admin: false);
        T result = default!;
        await fx.WithScope(async sp =>
        {
            var service = new UsageService(
                sp.GetRequiredService<Subscriptions.Contracts.Subscribers.ISubscriberIdProvider>(),
                sp.GetRequiredService<Subscriptions.Domains.Subscriptions.Entitlements.PlanEntitlementService>(),
                sp.GetRequiredService<IUsageCounterStore>(),
                sp.GetRequiredService<SubscriptionsDbContext>(),
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions { Entitlements = { Enforce = enforce } }),
                sp.GetRequiredService<UsageMetrics>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UsageService>.Instance);
            result = await act(new SingleService(service));
        });
        return result;
    }

    private sealed class SingleService(UsageService service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(UsageService) ? service : null;
    }

    [Fact]
    public async Task Quota_consumes_up_to_the_limit_then_denies_without_consuming()
    {
        var plan = await PlanAsync("Meter-M1", 100m, 0, ("api_calls", 3));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);

        Task<QuotaResult> Consume(long qty = 1) =>
            AsSubscriberAsync(subscriberId, sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("api_calls", qty));

        (await Consume()).Should().Be(new QuotaResult(true, 1, 3));
        (await Consume(2)).Should().Be(new QuotaResult(true, 3, 3));
        (await Consume()).Should().Be(new QuotaResult(false, 3, 3));
        (await AsSubscriberAsync(subscriberId, sp => ((UsageService)sp.GetService(typeof(UsageService))!).GetUsageAsync("api_calls")))
            .Should().Be(3, "the denied request gave its unit back");
    }

    [Fact]
    public async Task Racing_requests_never_exceed_the_limit()
    {
        var plan = await PlanAsync("Meter-M2", 100m, 0, ("exports", 5));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            AsSubscriberAsync(subscriberId, sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("exports", 1))));

        results.Count(r => r.Allowed).Should().Be(5);
    }

    [Fact]
    public async Task Not_enforced_counts_usage_but_never_denies()
    {
        var plan = await PlanAsync("Meter-M3", 100m, 0, ("seats", 1));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);

        var second = 0L;
        for (var i = 0; i < 3; i++)
        {
            var r = await AsSubscriberAsync(subscriberId,
                sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("seats", 1), enforce: false);
            r.Allowed.Should().BeTrue();
            second = r.Used;
        }

        second.Should().Be(3);
    }

    [Fact]
    public async Task Feature_outside_the_plan_is_denied_when_enforced()
    {
        var plan = await PlanAsync("Meter-M4", 100m);
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);

        var result = await AsSubscriberAsync(subscriberId,
            sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("api_calls", 1));

        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Store_failure_fails_open()
    {
        var plan = await PlanAsync("Meter-M5", 100m, 0, ("api_calls", 1));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);
        fx.Http.SetUser(subscriberId, admin: false);
        QuotaResult result = default;
        await fx.WithScope(async sp =>
        {
            var service = new UsageService(
                sp.GetRequiredService<Subscriptions.Contracts.Subscribers.ISubscriberIdProvider>(),
                sp.GetRequiredService<Subscriptions.Domains.Subscriptions.Entitlements.PlanEntitlementService>(),
                new ThrowingStore(),
                sp.GetRequiredService<SubscriptionsDbContext>(),
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions { Entitlements = { Enforce = true } }),
                sp.GetRequiredService<UsageMetrics>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UsageService>.Instance);
            result = await service.TryConsumeAsync("api_calls", 5);
        });

        result.Allowed.Should().BeTrue("Redis being down must never fail the request");
    }

    private sealed class ThrowingStore : IUsageCounterStore
    {
        public Task<long> AddAsync(string counterKey, long delta, TimeSpan? ttl, CancellationToken ct = default) => throw new InvalidOperationException("redis down");
        public Task<long?> GetAsync(string counterKey, CancellationToken ct = default) => throw new InvalidOperationException("redis down");
        public Task<IReadOnlyList<string>> DrainDirtyAsync(int max, CancellationToken ct = default) => throw new InvalidOperationException("redis down");
        public Task MarkDirtyAsync(IEnumerable<string> counterKeys, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Flush_persists_counters_and_a_lost_counter_resumes_from_the_database()
    {
        var plan = await PlanAsync("Meter-M6", 100m, 0, ("storage_ops", 10));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);
        await AsSubscriberAsync(subscriberId, sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("storage_ops", 4));

        await fx.Services.GetRequiredService<IUsageFlusher>().FlushAsync(100);

        var period = UsagePeriods.KeyFor(QuotaResetPeriod.Monthly, DateTimeOffset.UtcNow);
        (await fx.WithDb(db => db.UsageRecords.AsNoTracking()
            .SingleAsync(x => x.SubscriberId == subscriberId && x.Key == "storage_ops" && x.PeriodKey == period)))
            .Quantity.Should().Be(4);

        // Redis lost everything: a fresh store restarts from the persisted 4, not from 0.
        fx.Http.SetUser(subscriberId, admin: false);
        QuotaResult after = default;
        await fx.WithScope(async sp =>
        {
            var service = new UsageService(
                sp.GetRequiredService<Subscriptions.Contracts.Subscribers.ISubscriberIdProvider>(),
                sp.GetRequiredService<Subscriptions.Domains.Subscriptions.Entitlements.PlanEntitlementService>(),
                new InMemoryUsageCounterStore(),
                sp.GetRequiredService<SubscriptionsDbContext>(),
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions { Entitlements = { Enforce = true } }),
                sp.GetRequiredService<UsageMetrics>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UsageService>.Instance);
            after = await service.TryConsumeAsync("storage_ops", 1);
        });

        after.Used.Should().Be(5);
    }

    [Fact]
    public async Task Flush_upserts_a_mixed_batch_of_new_and_existing_records_in_one_pass()
    {
        var store = fx.Services.GetRequiredService<IUsageCounterStore>();
        var period = UsagePeriods.KeyFor(QuotaResetPeriod.Monthly, DateTimeOffset.UtcNow);
        var existingSubscriber = Guid.NewGuid();
        var newSubscribers = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();

        await store.AddAsync(UsagePeriods.CounterKey(existingSubscriber, period, "flush_a"), 2, null);
        await fx.Services.GetRequiredService<IUsageFlusher>().FlushAsync(100);

        await store.AddAsync(UsagePeriods.CounterKey(existingSubscriber, period, "flush_a"), 3, null); // existing: 2 -> 5
        foreach (var id in newSubscribers)
        {
            await store.AddAsync(UsagePeriods.CounterKey(id, period, "flush_a"), 7, null);
            await store.AddAsync(UsagePeriods.CounterKey(id, period, "flush_b"), 1, null);
        }

        (await fx.Services.GetRequiredService<IUsageFlusher>().FlushAsync(100)).Should().BeGreaterThanOrEqualTo(7);

        var rows = await fx.WithDb(db => db.UsageRecords.AsNoTracking()
            .Where(x => x.PeriodKey == period && (x.SubscriberId == existingSubscriber || newSubscribers.Contains(x.SubscriberId)))
            .ToListAsync());
        rows.Should().HaveCount(7, "one row per (subscriber, feature, period): updated in place, never duplicated");
        rows.Single(x => x.SubscriberId == existingSubscriber).Quantity.Should().Be(5);
        rows.Where(x => x.Key == "flush_a" && newSubscribers.Contains(x.SubscriberId)).Should().OnlyContain(x => x.Quantity == 7);
        rows.Where(x => x.Key == "flush_b").Should().OnlyContain(x => x.Quantity == 1);
    }

    [Fact]
    public async Task Concurrent_requests_after_a_lost_counter_never_slip_under_the_limit()
    {
        var plan = await PlanAsync("Meter-M7", 100m, 0, ("lost_ops", 5));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);
        await AsSubscriberAsync(subscriberId, sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("lost_ops", 5));
        await fx.Services.GetRequiredService<IUsageFlusher>().FlushAsync(100); // the database now says 5/5

        // Redis lost the counter: a fresh store. Two requests racing to re-create it must both see the real 5.
        var fresh = new InMemoryUsageCounterStore();
        fx.Http.SetUser(subscriberId, admin: false);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => fx.WithScopeResult(async sp =>
            await new UsageService(
                sp.GetRequiredService<Subscriptions.Contracts.Subscribers.ISubscriberIdProvider>(),
                sp.GetRequiredService<Subscriptions.Domains.Subscriptions.Entitlements.PlanEntitlementService>(),
                fresh,
                sp.GetRequiredService<SubscriptionsDbContext>(),
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions { Entitlements = { Enforce = true } }),
                sp.GetRequiredService<UsageMetrics>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UsageService>.Instance).TryConsumeAsync("lost_ops", 1))));

        results.Should().OnlyContain(r => !r.Allowed, "the quota was already used up");
        var period = UsagePeriods.KeyFor(QuotaResetPeriod.Monthly, DateTimeOffset.UtcNow);
        (await fresh.GetAsync(UsagePeriods.CounterKey(subscriberId, period, "lost_ops"))).Should().Be(5);
    }

    [Fact]
    public async Task Monthly_counters_reset_with_the_calendar_month_and_never_counters_do_not()
    {
        var now = new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.Zero);

        UsagePeriods.KeyFor(QuotaResetPeriod.Monthly, now).Should().Be("2026-12");
        UsagePeriods.KeyFor(QuotaResetPeriod.Monthly, now.AddMinutes(2)).Should().Be("2027-01");
        UsagePeriods.KeyFor(QuotaResetPeriod.Never, now.AddYears(3)).Should().Be("all");
        UsagePeriods.ResetsAt(QuotaResetPeriod.Monthly, now).Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        UsagePeriods.ResetsAt(QuotaResetPeriod.Never, now).Should().BeNull();
        UsagePeriods.TryParseCounterKey(UsagePeriods.CounterKey(Guid.NewGuid(), "2026-12", "a:b"), out _, out var period, out var feature)
            .Should().BeTrue();
        (period, feature).Should().Be(("2026-12", "a:b"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task My_usage_lists_metered_features_with_limit_and_reset()
    {
        var plan = await PlanAsync("Meter-M7", 100m, 0, ("reports", null), ("api_calls", 100));
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);
        await AsSubscriberAsync(subscriberId, sp => ((UsageService)sp.GetService(typeof(UsageService))!).TryConsumeAsync("api_calls", 7));

        var usage = await fx.SendAs(subscriberId, new GetMyUsageQuery(subscriberId));

        var entry = usage.Should().ContainSingle().Which;
        entry.Key.Should().Be("api_calls");
        entry.Limit.Should().Be(100);
        entry.Used.Should().Be(7);
        entry.ResetPeriod.Should().Be("Monthly");
        entry.ResetsAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Trialing_subscriber_has_the_plan_entitlements()
    {
        var plan = await PlanAsync("Trial-T6", 100m, trialDays: 5, ("reports", null));
        var subscriberId = Guid.NewGuid();
        await fx.SendAs(subscriberId, new StartTrialCommand(subscriberId, plan.Id));
        fx.Http.SetUser(subscriberId, admin: false);
        var has = false;
        await fx.WithScope(async sp =>
        {
            var service = new Subscriptions.Domains.Subscriptions.Entitlements.PlanEntitlementService(
                sp.GetRequiredService<SubscriptionsDbContext>(),
                sp.GetRequiredService<Subscriptions.Contracts.Subscribers.ISubscriberIdProvider>(),
                sp.GetRequiredService<Subscriptions.Domains.Subscriptions.Entitlements.IEntitlementCache>(),
                Microsoft.Extensions.Options.Options.Create(new SubscriptionOptions { Entitlements = { Enforce = true } }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Subscriptions.Domains.Subscriptions.Entitlements.PlanEntitlementService>.Instance);
            has = await service.HasFeatureAsync("reports");
        });

        has.Should().BeTrue();
    }
}
