using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Enums;
using Shared.Core.Exceptions;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Subscriptions.Contracts.Ledger.Enums;
using Subscriptions.Contracts.Ledger.Queries;
using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.Ledger.Features.GetBillingDashboard;
using Subscriptions.Domains.Ledger.Models;
using Subscriptions.Domains.Subscriptions.Features.GetAdminSubscriptions;
using Subscriptions.Domains.Subscriptions.Jobs;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// Admin billing console against real PostgreSQL: search + Subscriber-360, the immutable ledger (same transaction as
/// the money event), manual/partial payments, comp/reactivate/extend, duplicate proofs, the dashboard and the digest.
/// </summary>
[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class AdminBillingTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    // ----- search -----

    [Fact]
    public async Task Search_filters_by_text_status_plan_and_amount_in_sql_and_pages()
    {
        var tag = Tag();
        var plan = await fx.CreatePlanAsync($"Search-{tag}", 100m);
        var bigPlan = await fx.CreatePlanAsync($"SearchBig-{tag}", 900m);

        var alice = Person($"Alice {tag}", $"alice.{tag}@acme.test");
        var bob = Person($"Bob {tag}", $"bob.{tag}@other.test");
        var carol = Person($"Carol {tag}", $"carol.{tag}@acme.test");

        await _scenario.CreateAsync(alice, plan);                 // PendingApproval, 100
        await ActivateAsync(bob, plan);                           // Active, 100
        await ActivateAsync(carol, bigPlan);                      // Active, 900

        var byText = await Search(new GetAdminSubscriptionsQuery { Search = $"ACME.TEST" });
        byText.Result!.Select(x => x.SubscriberId).Should().BeEquivalentTo([alice, carol], "email search is case-insensitive contains");
        byText.Result!.Single(x => x.SubscriberId == alice).SubscriberName.Should().Be($"Alice {tag}");

        (await Search(new GetAdminSubscriptionsQuery { Search = $"bob {tag}" })).Result!.Single().SubscriberId.Should().Be(bob);
        (await Search(new GetAdminSubscriptionsQuery { Search = tag, Status = SubscriptionStatus.Active }))
            .Result!.Select(x => x.SubscriberId).Should().BeEquivalentTo([bob, carol]);
        (await Search(new GetAdminSubscriptionsQuery { Search = tag, PlanId = bigPlan.Id })).Result!.Single().SubscriberId.Should().Be(carol);
        (await Search(new GetAdminSubscriptionsQuery { Search = tag, MinAmount = 500m })).Result!.Single().SubscriberId.Should().Be(carol);
        (await Search(new GetAdminSubscriptionsQuery { Search = carol.ToString() })).Result!.Single().SubscriberId.Should().Be(carol);

        var renewalFrom = DateTimeOffset.UtcNow;
        (await Search(new GetAdminSubscriptionsQuery { Search = tag, RenewalFrom = renewalFrom, RenewalTo = renewalFrom.AddMonths(2) }))
            .Result!.Should().HaveCount(2, "only the active ones have a renewal date");

        var page = await Search(new GetAdminSubscriptionsQuery
        {
            Search = tag, PageNumber = 2, PageSize = 2, SortColumn = "SubscriberDisplayName", SortBy = SortBy.Ascending
        });
        page.PageInfoResult.TotalItems.Should().Be(3);
        page.Result!.Single().SubscriberName.Should().Be($"Carol {tag}");

        var sql = await fx.WithDb(db => Task.FromResult(GetAdminSubscriptionsHandler
            .Filter(db.Subscriptions, new GetAdminSubscriptionsQuery { Search = "acme", MinAmount = 1m })
            .ToQueryString()));
        sql.Should().Contain("ILIKE").And.Contain("subscriber_email").And.Contain("price >=",
            "the text search and filters are translated to SQL, never evaluated in memory");
    }

    [Fact]
    public async Task Search_escapes_like_wildcards()
    {
        var tag = Tag();
        var plan = await fx.CreatePlanAsync($"Wild-{tag}", 100m);
        await _scenario.CreateAsync(Person($"Percent {tag}", $"p.{tag}@x.test"), plan);

        (await Search(new GetAdminSubscriptionsQuery { Search = $"%{tag}" })).Result.Should().BeEmpty();
        (await Search(new GetAdminSubscriptionsQuery { Search = $"_ercent {tag}" })).Result.Should().BeEmpty();
    }

    // ----- Subscriber-360 -----

    [Fact]
    public async Task Subscriber_360_aggregates_payments_invoices_ledger_transitions_and_limits()
    {
        var tag = Tag();
        var plan = await fx.CreatePlanAsync($"360-{tag}", 115m);
        var subscriber = Person($"Dana {tag}", $"dana.{tag}@x.test");
        var created = await _scenario.CreateAsync(subscriber, plan);
        await fx.SendAsAdmin(new RecordManualPaymentCommand(created.SubscriptionId, TestIds.Admin, 15m, "Cash", "R-1"));
        await _scenario.UploadActivationProofAsync(subscriber, created.SubscriptionId, created.PaymentId);
        await fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId));

        var view = await fx.SendAsAdmin(new GetAdminSubscriptionByIdQuery(created.SubscriptionId));

        view.Subscription.Status.Should().Be("Active");
        view.Subscription.SubscriberName.Should().Be($"Dana {tag}");
        var payment = view.Payments.Single();
        payment.Should().BeEquivalentTo(new { Status = "Paid", Amount = 115m, AmountReceived = 115m, OutstandingAmount = 0m, DuplicateProof = false });
        payment.ProofAttachmentId.Should().NotBeNull();
        payment.ProofContentHash.Should().HaveLength(64);
        payment.Receipts.Should().ContainSingle(r => r.Amount == 15m && r.Method == "Cash" && r.Reference == "R-1");

        view.Invoices.Should().ContainSingle(i => i.Type == "Activation" && i.TotalAmount == 115m);
        view.CreditNotes.Should().BeEmpty();
        view.Transitions.Select(t => t.ToStatus).Should().Equal("PendingApproval", "Active");
        view.Ledger.Should().HaveCount(2, "one entry per amount received: the manual 15 and the approved remainder 100");
        view.Ledger.Sum(x => x.Amount).Should().Be(115m);
        view.Ledger.Single(x => x.Amount == 100m).InvoiceId.Should().Be(view.Invoices.Single().InvoiceId);
        view.LedgerTotals.Single().Should().BeEquivalentTo(new { Currency = "SAR", Gross = 115m, Net = 115m });
        view.Limits.Status.Should().Be("Active");
        view.Limits.MaxItems.Should().Be(10);
    }

    [Fact]
    public async Task Subscriber_360_of_an_unknown_subscription_is_not_found()
    {
        var act = () => fx.SendAsAdmin(new GetAdminSubscriptionByIdQuery(Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----- ledger: same transaction as the money event -----

    [Fact]
    public async Task Approval_writes_exactly_one_payment_entry_in_the_same_transaction()
    {
        var plan = await fx.CreatePlanAsync($"Ledger-{Tag()}", 230m);
        var (_, subscriptionId, approval) = await _scenario.ActiveAsync(plan);

        var entries = await LedgerOf(subscriptionId);

        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Type = LedgerEntryType.Payment,
            Amount = 230m,
            Currency = "SAR",
            PaymentId = approval.PaymentId,
            InvoiceId = approval.InvoiceId,
            ActorId = TestIds.Admin,
            PlanId = plan.Id,
            PaymentMethod = "BankTransfer"
        });
    }

    [Fact]
    public async Task A_failed_approval_writes_no_ledger_entry_and_no_invoice()
    {
        var plan = await fx.CreatePlanAsync($"LedgerFail-{Tag()}", 100m);
        var subscriber = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriber, plan);
        await _scenario.UploadActivationProofAsync(subscriber, created.SubscriptionId, created.PaymentId);

        var stale = () => fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId, Version: 999));

        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
        (await LedgerOf(created.SubscriptionId)).Should().BeEmpty();
        (await _scenario.InvoicesAsync(created.SubscriptionId)).Should().BeEmpty();
        (await _scenario.PaymentsAsync(created.SubscriptionId)).Single().Status.Should().Be(PaymentStatus.Submitted);
    }

    [Fact]
    public async Task Refund_processing_and_credit_notes_are_booked_with_negative_amounts()
    {
        var plan = await fx.CreatePlanAsync($"LedgerRefund-{Tag()}", 1150m, cycle: 12);
        var (subscriberId, subscriptionId, approval) = await _scenario.ActiveAsync(plan);

        var note = await fx.SendAsAdmin(new IssueCreditNoteCommand(approval.InvoiceId, TestIds.Admin, "goodwill", 115m));

        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, "test"));
        var refundId = await fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));
        await fx.SendAsAdmin(new AdminReviewRefundCommand(refundId, TestIds.Admin, true, null));
        await fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "TX-1"));

        var entries = await LedgerOf(subscriptionId);
        entries.Single(x => x.Type == LedgerEntryType.CreditNote).Should().BeEquivalentTo(new { Amount = -115m, InvoiceId = note.CreditNoteId });
        var refundEntry = entries.Single(x => x.Type == LedgerEntryType.Refund);
        refundEntry.Amount.Should().BeNegative();
        refundEntry.RefundId.Should().Be(refundId);
        refundEntry.Reference.Should().Be("TX-1");
        entries.Should().HaveCount(3, "payment + standalone credit note + refund; refund credit notes are not booked twice");
    }

    [Fact]
    public async Task Ledger_rows_cannot_be_updated_or_deleted_through_ef()
    {
        var plan = await fx.CreatePlanAsync($"LedgerImmutable-{Tag()}", 100m);
        var (_, subscriptionId, _) = await _scenario.ActiveAsync(plan);

        var delete = () => fx.WithDb(async db =>
        {
            db.LedgerEntries.Remove(await db.LedgerEntries.FirstAsync(x => x.SubscriptionId == subscriptionId));
            return await db.SaveChangesAsync();
        });

        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
        (await LedgerOf(subscriptionId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Ledger_query_filters_by_type_subscriber_and_range()
    {
        var plan = await fx.CreatePlanAsync($"LedgerQuery-{Tag()}", 100m);
        var (subscriberId, _, _) = await _scenario.ActiveAsync(plan);

        var page = await fx.SendAsAdmin(new GetLedgerEntriesQuery
        {
            SubscriberId = subscriberId,
            Type = LedgerEntryType.Payment,
            From = DateTimeOffset.UtcNow.AddHours(-1),
            To = DateTimeOffset.UtcNow.AddHours(1)
        });
        page.Result!.Should().ContainSingle().Which.Amount.Should().Be(100m);

        (await fx.SendAsAdmin(new GetLedgerEntriesQuery { SubscriberId = subscriberId, Type = LedgerEntryType.Refund }))
            .Result.Should().BeEmpty();
    }

    // ----- manual / partial payments -----

    [Fact]
    public async Task Manual_partial_payments_accumulate_then_activate_with_invoice_and_ledger()
    {
        var plan = await fx.CreatePlanAsync($"Manual-{Tag()}", 1150m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);

        var first = await fx.SendAsAdmin(new RecordManualPaymentCommand(
            created.SubscriptionId, TestIds.Admin, 500m, "BankTransfer", "STMT-1", DateTimeOffset.UtcNow.AddDays(-1)));

        first.Should().BeEquivalentTo(new
        {
            Completed = false,
            PaymentStatus = "PartiallyPaid",
            AmountReceived = 500m,
            OutstandingAmount = 650m,
            SubscriptionStatus = "PendingApproval",
            InvoiceId = (Guid?)null
        });
        (await _scenario.InvoicesAsync(created.SubscriptionId)).Should().BeEmpty("no invoice before the amount is complete");

        var over = () => fx.SendAsAdmin(new RecordManualPaymentCommand(created.SubscriptionId, TestIds.Admin, 650.01m, "Cash"));
        await over.Should().ThrowAsync<BadRequestException>();
        (await LedgerOf(created.SubscriptionId)).Should().ContainSingle("the rejected overpayment wrote nothing");

        var last = await fx.SendAsAdmin(new RecordManualPaymentCommand(created.SubscriptionId, TestIds.Admin, 650m, "Cash", "R-2"));

        last.Should().BeEquivalentTo(new { Completed = true, PaymentStatus = "Paid", OutstandingAmount = 0m, SubscriptionStatus = "Active" });
        var subscription = await _scenario.LoadAsync(created.SubscriptionId);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.RenewalDate.Should().NotBeNull();

        var invoice = (await _scenario.InvoicesAsync(created.SubscriptionId)).Single();
        invoice.Should().BeEquivalentTo(new { Type = InvoiceType.Activation, TotalAmount = 1150m, PaymentId = created.PaymentId, Id = last.InvoiceId!.Value });

        var ledger = await LedgerOf(created.SubscriptionId);
        ledger.Select(x => x.Amount).Should().BeEquivalentTo([500m, 650m]);
        ledger.Single(x => x.Amount == 650m).Should().BeEquivalentTo(new { InvoiceId = invoice.Id, PaymentMethod = "Cash", Reference = "R-2" });
        ledger.Single(x => x.Amount == 500m).InvoiceId.Should().BeNull();

        var receipts = await fx.WithDb(db => db.PaymentReceipts.AsNoTracking().Where(x => x.SubscriptionId == created.SubscriptionId).ToListAsync());
        receipts.Should().HaveCount(2);

        var audit = await fx.WithDb(db => db.AdminAuditLogs.AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId && x.Action == "RecordManualPayment").ToListAsync());
        audit.Should().HaveCount(2);
        audit.Select(x => x.Amount).Should().BeEquivalentTo(new decimal?[] { 500m, 650m });
    }

    [Fact]
    public async Task Manual_payment_settles_a_renewal_like_a_proof_approval()
    {
        var plan = await fx.CreatePlanAsync($"ManualRenewal-{Tag()}", 100m);
        var (_, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        await fx.Services.GetRequiredService<Subscriptions.Domains.Subscriptions.Services.IRenewalOrchestrator>()
            .ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));

        var result = await fx.SendAsAdmin(new RecordManualPaymentCommand(subscriptionId, TestIds.Admin, 100m, "Cash"));

        result.Completed.Should().BeTrue();
        var subscription = await _scenario.LoadAsync(subscriptionId);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.CurrentPeriodStart.Should().BeCloseTo(renewal, TimeSpan.FromSeconds(1));
        (await _scenario.InvoicesAsync(subscriptionId)).Select(x => x.Type).Should().Equal(InvoiceType.Activation, InvoiceType.Renewal);
        (await LedgerOf(subscriptionId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Manual_payment_with_a_stale_version_writes_nothing()
    {
        var plan = await fx.CreatePlanAsync($"ManualStale-{Tag()}", 100m);
        var created = await _scenario.CreateAsync(Guid.NewGuid(), plan);

        var act = () => fx.SendAsAdmin(new RecordManualPaymentCommand(created.SubscriptionId, TestIds.Admin, 10m, "Cash", Version: 42));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await LedgerOf(created.SubscriptionId)).Should().BeEmpty();
        (await _scenario.PaymentsAsync(created.SubscriptionId)).Single().AmountReceived.Should().Be(0m);
    }

    // ----- comp / reactivate / extend -----

    [Fact]
    public async Task Comp_activates_with_a_zero_invoice_and_a_zero_adjustment()
    {
        var plan = await fx.CreatePlanAsync($"Comp-{Tag()}", 100m);
        var premium = await fx.CreatePlanAsync($"CompPremium-{Tag()}", 999m);
        var created = await _scenario.CreateAsync(Guid.NewGuid(), plan);

        var result = await fx.SendAsAdmin(new CompSubscriptionCommand(created.SubscriptionId, TestIds.Admin, premium.Id, 2, "partner"));

        result.Status.Should().Be("Active");
        result.PlanId.Should().Be(premium.Id);
        result.RenewalDate.Should().BeCloseTo(result.StartDate.AddMonths(2), TimeSpan.FromSeconds(1));

        var payments = await _scenario.PaymentsAsync(created.SubscriptionId);
        payments.Single(x => x.Id == created.PaymentId).Status.Should().Be(PaymentStatus.Failed, "the owed activation payment is waived");
        payments.Single(x => x.Id == result.PaymentId).Should().BeEquivalentTo(new { Amount = 0m, Status = PaymentStatus.Paid });

        (await _scenario.InvoicesAsync(created.SubscriptionId)).Single().TotalAmount.Should().Be(0m);
        (await LedgerOf(created.SubscriptionId)).Single().Should().BeEquivalentTo(new
        {
            Type = LedgerEntryType.Adjustment,
            Amount = 0m,
            PaymentId = result.PaymentId,
            InvoiceId = result.InvoiceId
        });
        (await AuditActions(created.SubscriptionId)).Should().Contain("CompSubscription");
    }

    [Fact]
    public async Task Reactivated_subscription_gets_a_new_payment_and_activates_through_the_normal_path()
    {
        var plan = await fx.CreatePlanAsync($"Reactivate-{Tag()}", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, "test"));

        var reopened = await fx.SendAsAdmin(new ReactivateSubscriptionCommand(subscriptionId, TestIds.Admin, "customer came back"));

        reopened.Status.Should().Be("PendingApproval");
        reopened.AmountDue.Should().Be(100m);
        await _scenario.UploadActivationProofAsync(subscriberId, subscriptionId, reopened.PaymentId);
        await fx.SendAsAdmin(new ApproveSubscriptionCommand(subscriptionId, TestIds.Admin, reopened.PaymentId));

        (await _scenario.LoadAsync(subscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
        (await LedgerOf(subscriptionId)).Should().HaveCount(2);
        (await AuditActions(subscriptionId)).Should().Contain("ReactivateSubscription");
    }

    [Fact]
    public async Task Reactivation_is_refused_while_the_subscriber_has_another_open_subscription()
    {
        var plan = await fx.CreatePlanAsync($"ReactivateConflict-{Tag()}", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, "test"));
        await _scenario.CreateAsync(subscriberId, plan);

        var act = () => fx.SendAsAdmin(new ReactivateSubscriptionCommand(subscriptionId, TestIds.Admin, "again"));

        await act.Should().ThrowAsync<ConflictException>();
        (await _scenario.LoadAsync(subscriptionId)).Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Extend_shifts_the_renewal_date_and_is_audited_and_logged()
    {
        var plan = await fx.CreatePlanAsync($"Extend-{Tag()}", 100m);
        var (_, subscriptionId, approval) = await _scenario.ActiveAsync(plan);

        var result = await fx.SendAsAdmin(new ExtendSubscriptionCommand(subscriptionId, TestIds.Admin, 10, "outage"));

        result.RenewalDate.Should().BeCloseTo(approval.RenewalDate.AddDays(10), TimeSpan.FromSeconds(1));
        (await AuditActions(subscriptionId)).Should().Contain("ExtendSubscription");
        var log = await fx.WithDb(db => db.TransitionLogs.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).ToListAsync());
        log.Should().Contain(x => x.Reason != null && x.Reason.Contains("Extended by 10 day(s): outage"));
    }

    // ----- duplicate proofs + queue -----

    [Fact]
    public async Task Identical_proof_content_is_flagged_as_duplicate_without_blocking()
    {
        var tag = Tag();
        var plan = await fx.CreatePlanAsync($"Dup-{tag}", 100m);
        byte[] sameFile = [0x89, 0x50, 0x4E, 0x47, .. Guid.NewGuid().ToByteArray()];

        var first = Guid.NewGuid();
        var firstSub = await _scenario.CreateAsync(first, plan);
        await _scenario.UploadActivationProofAsync(first, firstSub.SubscriptionId, firstSub.PaymentId, sameFile);

        var second = Person($"Dup {tag}", $"dup.{tag}@x.test");
        var secondSub = await _scenario.CreateAsync(second, plan);
        await _scenario.UploadActivationProofAsync(second, secondSub.SubscriptionId, secondSub.PaymentId, sameFile);

        var payments = await fx.WithDb(db => db.Payments.AsNoTracking()
            .Where(x => x.Id == firstSub.PaymentId || x.Id == secondSub.PaymentId).ToListAsync());
        payments.Single(x => x.Id == firstSub.PaymentId).DuplicateProof.Should().BeFalse();
        var duplicate = payments.Single(x => x.Id == secondSub.PaymentId);
        duplicate.Status.Should().Be(PaymentStatus.Submitted, "a duplicate is a warning, not a block");
        duplicate.DuplicateProof.Should().BeTrue();
        duplicate.DuplicateOfPaymentId.Should().Be(firstSub.PaymentId);
        duplicate.ProofContentHash.Should().Be(payments.Single(x => x.Id == firstSub.PaymentId).ProofContentHash);

        var queue = await fx.SendAsAdmin(new GetPendingPaymentsQuery { Search = $"dup.{tag}", DuplicateProofOnly = true });
        var row = queue.Result!.Single();
        row.Should().BeEquivalentTo(new
        {
            PaymentId = secondSub.PaymentId,
            DuplicateProof = true,
            DuplicateOfPaymentId = firstSub.PaymentId,
            SubscriberName = $"Dup {tag}",
            SubscriberEmail = $"dup.{tag}@x.test",
            PlanId = plan.Id,
            Amount = 100m,
            Purpose = "Activation"
        });
        row.PlanName.Should().StartWith("Dup-");
        row.AgeHours.Should().BeGreaterThanOrEqualTo(0);

        var approval = await fx.SendAsAdmin(new ApproveSubscriptionCommand(secondSub.SubscriptionId, TestIds.Admin, secondSub.PaymentId));
        approval.Status.Should().Be("Active");
        var audit = await fx.WithDb(db => db.AdminAuditLogs.AsNoTracking()
            .SingleAsync(x => x.TargetId == secondSub.SubscriptionId && x.Action == "ApproveSubscription"));
        audit.AfterJson.Should().Contain("\"DuplicateProof\": true");
    }

    [Fact]
    public async Task Pending_queue_filters_by_amount_method_purpose_and_date()
    {
        var tag = Tag();
        var cheap = await fx.CreatePlanAsync($"QueueCheap-{tag}", 10m);
        var pricey = await fx.CreatePlanAsync($"QueuePricey-{tag}", 5000m);
        var a = Person($"Queue {tag} A", $"qa.{tag}@x.test");
        var b = Person($"Queue {tag} B", $"qb.{tag}@x.test");
        var sa = await _scenario.CreateAsync(a, cheap);
        var sb = await _scenario.CreateAsync(b, pricey);
        await _scenario.UploadActivationProofAsync(a, sa.SubscriptionId, sa.PaymentId);
        await _scenario.UploadActivationProofAsync(b, sb.SubscriptionId, sb.PaymentId);

        (await Queue(new GetPendingPaymentsQuery { Search = tag, MinAmount = 1000m })).Single().PaymentId.Should().Be(sb.PaymentId);
        (await Queue(new GetPendingPaymentsQuery { Search = tag, MaxAmount = 100m })).Single().PaymentId.Should().Be(sa.PaymentId);
        (await Queue(new GetPendingPaymentsQuery { Search = tag, Method = "banktransfer" })).Should().HaveCount(2);
        (await Queue(new GetPendingPaymentsQuery { Search = tag, Method = "Cash" })).Should().BeEmpty();
        (await Queue(new GetPendingPaymentsQuery { Search = tag, Purpose = "Renewal" })).Should().BeEmpty();
        (await Queue(new GetPendingPaymentsQuery { Search = tag, SubmittedFrom = DateTimeOffset.UtcNow.AddMinutes(-10) })).Should().HaveCount(2);
        (await Queue(new GetPendingPaymentsQuery { Search = tag, SubmittedTo = DateTimeOffset.UtcNow.AddHours(-1) })).Should().BeEmpty();
    }

    // ----- dashboard -----

    [Fact]
    public async Task Dashboard_computes_mrr_and_revenue_from_the_ledger_and_is_cached_per_range()
    {
        // A currency and a date range no other test uses, so the shared database does not interfere.
        var monthly = await fx.CreatePlanAsync($"DashMonthly-{Tag()}", 100m, currency: "NOK");
        var annual = await fx.CreatePlanAsync($"DashAnnual-{Tag()}", 1200m, currency: "NOK", cycle: 12);
        await _scenario.ActiveAsync(monthly);
        await _scenario.ActiveAsync(annual);
        var comped = await _scenario.CreateAsync(Guid.NewGuid(), monthly);
        await fx.SendAsAdmin(new CompSubscriptionCommand(comped.SubscriptionId, TestIds.Admin, monthly.Id, 1, "free month"));

        var from = new DateTimeOffset(2031, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);
        await SeedLedgerAsync(from, monthly, annual);

        var dashboard = await fx.SendAsAdmin(new GetBillingDashboardQuery { From = from, To = to });

        var xts = dashboard.RecurringRevenue.Single(x => x.Currency == "NOK");
        xts.Mrr.Should().Be(200m, "100 monthly + 1200 / 12; the complimentary subscription earns nothing");
        xts.Arr.Should().Be(2400m);
        xts.ActiveSubscriptions.Should().Be(2);

        dashboard.Revenue.Single(x => x.Currency == "NOK").Should().BeEquivalentTo(new
        {
            Gross = 1500m,
            Refunds = 200m,
            CreditNotes = 0m,
            Adjustments = 0m,
            Net = 1300m
        });
        dashboard.Revenue.Single(x => x.Currency == "XTU").Net.Should().Be(10m, "currencies are reported separately");
        dashboard.RevenueByPlan.Single(x => x.Currency == "NOK" && x.Key == annual.Id.ToString())
            .Should().BeEquivalentTo(new { Gross = 1200m, Refunds = 200m, Net = 1000m });
        dashboard.RevenueByPaymentMethod.Single(x => x.Currency == "NOK" && x.Key == "Cash").Gross.Should().Be(300m);
        dashboard.StatusCounts.Should().Contain(x => x.Status == "Active" && x.Count >= 3);

        await SeedLedgerAsync(from.AddDays(1), monthly, annual);
        var cached = await fx.SendAsAdmin(new GetBillingDashboardQuery { From = from, To = to });
        cached.Revenue.Single(x => x.Currency == "NOK").Gross.Should().Be(1500m, "the result is cached for the range (no invalidation)");

        await fx.WithScope(sp => sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>()
            .RemoveAsync(GetBillingDashboardHandler.CacheKey(from, to)));
        var fresh = await fx.SendAsAdmin(new GetBillingDashboardQuery { From = from, To = to });
        fresh.Revenue.Single(x => x.Currency == "NOK").Gross.Should().Be(3000m);
    }

    [Fact]
    public async Task Dashboard_counts_the_queue_and_new_subscriptions()
    {
        var plan = await fx.CreatePlanAsync($"DashQueue-{Tag()}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        await _scenario.ActiveAsync(plan);

        // A one-hour range around "now" that no other test caches.
        var from = DateTimeOffset.UtcNow.AddMinutes(-30).AddTicks(Random.Shared.Next(1, 10_000));
        var dashboard = await fx.SendAsAdmin(new GetBillingDashboardQuery { From = from, To = from.AddHours(1) });

        dashboard.PendingProofs.Should().BeGreaterThanOrEqualTo(1);
        dashboard.OldestPendingProofAgeHours.Should().NotBeNull();
        dashboard.NewSubscriptions.Should().BeGreaterThanOrEqualTo(1);
        dashboard.Revenue.Single(x => x.Currency == "SAR").Gross.Should().BeGreaterThanOrEqualTo(100m);
    }

    // ----- admin notifications + digest -----

    [Fact]
    public async Task Proof_notifications_go_to_the_configured_billing_team_not_the_sender_address()
    {
        var plan = await fx.CreatePlanAsync($"Notify-{Tag()}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        var before = AdminEmails(EmailTemplate.AdminPaymentProofReceived, SubscriptionsPostgresFixture.BillingTeamEmail);

        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);

        AdminEmails(EmailTemplate.AdminPaymentProofReceived, SubscriptionsPostgresFixture.BillingTeamEmail).Should().Be(before + 1);
        AdminEmails(EmailTemplate.AdminPaymentProofReceived, "admin@example.com").Should().Be(0, "Email:FromAddress is the sender, not a recipient");
    }

    [Fact]
    public async Task Digest_job_reports_the_queue_through_the_unit_of_work_runner_and_emails_the_billing_team()
    {
        var plan = await fx.CreatePlanAsync($"Digest-{Tag()}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        await fx.WithDb(db => db.Payments.Where(x => x.Id == created.PaymentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SubmittedAt, DateTimeOffset.UtcNow.AddDays(-3))));
        var before = AdminEmails(EmailTemplate.AdminBillingDigest, SubscriptionsPostgresFixture.BillingTeamEmail);

        var digest = await fx.Services.GetRequiredService<AdminBillingDigestJob>().ExecuteAsync(new JobCancellationToken(false));

        digest.PendingProofs.Should().BeGreaterThanOrEqualTo(1);
        digest.StaleProofs.Should().BeGreaterThanOrEqualTo(1);
        digest.OldestPendingAgeHours.Should().BeGreaterThanOrEqualTo(72);
        digest.RecipientCount.Should().Be(1);
        AdminEmails(EmailTemplate.AdminBillingDigest, SubscriptionsPostgresFixture.BillingTeamEmail).Should().Be(before + 1);

        typeof(AdminBillingDigestJob).GetConstructors().Single().GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(Shared.Core.Data.IUnitOfWorkRunner));
    }

    // ----- helpers -----

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private Guid Person(string name, string email)
    {
        var id = Guid.NewGuid();
        fx.Subscribers.People[id] = new SubscriberInfo(id, name, email);
        return id;
    }

    private async Task ActivateAsync(Guid subscriberId, Domains.Plans.Models.Plan plan)
    {
        var created = await _scenario.CreateAsync(subscriberId, plan);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        await fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId));
    }

    private Task<Shared.Contracts.Pagination.PaginatedResult<Subscriptions.Contracts.Subscriptions.DTOs.AdminSubscriptionListItemDto>> Search(
        GetAdminSubscriptionsQuery query) => fx.SendAsAdmin(query);

    private async Task<List<Subscriptions.Contracts.Subscriptions.DTOs.PendingPaymentDto>> Queue(GetPendingPaymentsQuery query) =>
        (await fx.SendAsAdmin(query)).Result!;

    private Task<List<LedgerEntry>> LedgerOf(Guid subscriptionId) =>
        fx.WithDb(db => db.LedgerEntries.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync());

    private Task<List<string>> AuditActions(Guid subscriptionId) =>
        fx.WithDb(db => db.AdminAuditLogs.AsNoTracking()
            .Where(x => x.TargetId == subscriptionId)
            .Select(x => x.Action)
            .ToListAsync());

    private int AdminEmails(EmailTemplate template, string address) =>
        fx.Notifications.Messages.Count(m => m.Email?.Template == template && m.EmailOverride == address);

    /// <summary>NOK: 300 cash + 1200 bank (annual plan) - 200 refund; XTU: 10. Inside [at, at + 1 month).</summary>
    private Task SeedLedgerAsync(DateTimeOffset at, Domains.Plans.Models.Plan monthly, Domains.Plans.Models.Plan annual) =>
        fx.WithDb(async db =>
        {
            var monthlySub = Subscription.Create(Guid.NewGuid(), PlanSnapshot.From(monthly, monthly.Price), 300m, at);
            var annualSub = Subscription.Create(Guid.NewGuid(), PlanSnapshot.From(annual, annual.Price), 1200m, at);
            var otherSub = Subscription.Create(Guid.NewGuid(), new PlanSnapshot(Guid.NewGuid(), "Other", 10m, "XTU", 1, null, null, null, "{}"), 10m, at);
            var refund = SubscriptionRefundRequest.Create(
                annualSub.Id, annualSub.SubscriberId, annual.Id, annualSub.PlanName, 12, at, at, at.AddMonths(12),
                200m, "NOK", RefundType.Prorated);

            db.LedgerEntries.AddRange(
                LedgerEntry.ForPayment(monthlySub, monthlySub.ActivationPayment, 300m, null, TestIds.Admin, at.AddHours(1), "Cash"),
                LedgerEntry.ForPayment(annualSub, annualSub.ActivationPayment, 1200m, null, TestIds.Admin, at.AddHours(2), "BankTransfer"),
                LedgerEntry.ForRefund(refund, null, TestIds.Admin, at.AddHours(3)),
                LedgerEntry.ForPayment(otherSub, otherSub.ActivationPayment, 10m, null, TestIds.Admin, at.AddHours(4), "Cash"));
            return await db.SaveChangesAsync();
        });
}
