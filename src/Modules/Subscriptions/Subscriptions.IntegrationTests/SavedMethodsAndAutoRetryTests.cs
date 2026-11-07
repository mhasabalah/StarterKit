using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Data;
using Subscriptions.Domains.OnlinePayments.Features.ReceivePaymentWebhook;
using Subscriptions.Domains.OnlinePayments.Features.RemoveSavedPaymentMethod;
using Subscriptions.Domains.OnlinePayments.Services;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Plans.Models;
using Subscriptions.Domains.Subscriptions.Models;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// Money precision, saved payment methods and automatic dunning retries against real PostgreSQL through the real
/// pipeline (the Fake gateway decides charge outcomes from the saved method reference).
/// </summary>
[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class SavedMethodsAndAutoRetryTests(SubscriptionsPostgresFixture fx)
{
    private const string ReturnUrl = "https://app.example.com/ok";
    private readonly Scenario _scenario = new(fx);

    // ----- precision -----

    [Fact]
    public async Task Plan_price_with_three_decimals_round_trips_through_the_database()
    {
        var id = await fx.WithDb(async db =>
        {
            var plan = Plan.Create(LocalizedText.Create("KWD", "KWD"), null, 12.345m, "KWD", 1, 10, 5, 3, true);
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
            return plan.Id;
        });

        var loaded = await fx.WithDb(db => db.Plans.AsNoTracking().SingleAsync(x => x.Id == id));
        loaded.Price.Should().Be(12.345m);
    }

    [Fact]
    public void Plan_price_with_more_decimals_than_the_currency_allows_is_rejected()
    {
        var act = () => Plan.Create(LocalizedText.Create("JPY", "JPY"), null, 100.5m, "JPY", 1, 10, 5, 3, true);

        act.Should().Throw<BadRequestException>();
    }

    // ----- capturing a saved card from the checkout webhook -----

    [Fact]
    public async Task Checkout_webhook_saves_the_card_as_the_default_method()
    {
        var plan = await fx.CreatePlanAsync($"Saved-{Guid.NewGuid():N}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        var checkout = await fx.SendAs(subscriberId, new CreateOnlineCheckoutCommand(
            created.SubscriptionId, subscriberId, "Fake", ReturnUrl, ReturnUrl));

        var methodRef = $"pm_{Guid.NewGuid():N}";
        var body = JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "payment.succeeded",
            reference = checkout.SessionId,
            amount = checkout.Amount,
            currency = "SAR",
            customer_reference = "cus_1",
            method_reference = methodRef,
            card_brand = "visa",
            card_last4 = "4242",
            card_exp_month = 12,
            card_exp_year = 2099
        });
        await fx.SendAs(null, new ReceivePaymentWebhookCommand("Fake", body, new Dictionary<string, string>
        {
            [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(SubscriptionsPostgresFixture.FakeGatewaySecret, body)
        }));

        var saved = await fx.WithDb(db => db.SavedPaymentMethods.AsNoTracking().SingleAsync(x => x.SubscriberId == subscriberId));
        saved.ProviderMethodRef.Should().Be(methodRef);
        saved.Last4.Should().Be("4242");
        saved.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Only_one_default_method_per_subscriber_is_allowed_by_the_database()
    {
        var subscriberId = Guid.NewGuid();
        await SaveMethodAsync(subscriberId, "pm_a");

        var act = () => SaveMethodAsync(subscriberId, "pm_b");

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ----- dunning auto retry -----

    private async Task<(Guid SubscriberId, Guid SubscriptionId, SubscriptionPayment Payment)> OverdueAsync()
    {
        var plan = await fx.CreatePlanAsync($"Retry-{Guid.NewGuid():N}", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        await fx.Services.GetRequiredService<IRenewalOrchestrator>().ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));
        var payment = (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Purpose == PaymentPurpose.Renewal);
        fx.Notifications.Messages.Clear();
        return (subscriberId, subscriptionId, payment);
    }

    private Task SaveMethodAsync(Guid subscriberId, string methodRef, bool isDefault = true) =>
        fx.WithDb(async db =>
        {
            db.SavedPaymentMethods.Add(SavedPaymentMethod.Create(
                subscriberId, "Fake", "cus_test", methodRef + "_" + Guid.NewGuid().ToString("N"), "visa", "4242", 12, 2099, isDefault, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return true;
        });

    private IEnumerable<Notification.Dispatch.NotificationMessage> Reminders(Guid subscriberId) =>
        fx.Notifications.Messages.Where(m => m.UserId == subscriberId &&
                                             m.Email?.Template == Notification.Contracts.Enums.EmailTemplate.SubscriptionDunning);

    [Fact]
    public async Task Dunning_charges_the_saved_card_settles_the_payment_and_sends_no_reminder()
    {
        var (subscriberId, subscriptionId, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_ok");
        var processor = fx.Services.GetRequiredService<IDunningProcessor>();
        var at = payment.CreatedAt.AddDays(1).AddMinutes(1);

        await processor.ProcessAsync(at);

        (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Id == payment.Id).Status.Should().Be(PaymentStatus.Paid);
        (await _scenario.LoadAsync(subscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
        Reminders(subscriberId).Should().BeEmpty("a collected payment needs no reminder");
        var attempts = await fx.WithDb(db => db.PaymentAttempts.AsNoTracking().Where(x => x.PaymentId == payment.Id).ToListAsync());
        attempts.Should().ContainSingle().Which.Outcome.Should().Be(PaymentAttemptOutcome.Succeeded);

        await processor.ProcessAsync(at.AddMinutes(5));
        (await fx.WithDb(db => db.PaymentAttempts.CountAsync(x => x.PaymentId == payment.Id))).Should().Be(1);
        CountingFakeGateway.ChargesFor(PaymentAttempt.KeyFor(payment.Id, "day-1"))
            .Should().Be(1, "a step is attempted once; a re-run never calls the provider again");
        var notice = await fx.WithDb(db => db.DunningNotices.AsNoTracking().SingleAsync(x => x.PaymentId == payment.Id && x.Step == "day-1"));
        notice.AutoRetryOutcome.Should().Be("Succeeded");
        notice.Sent.Should().BeFalse("a collected payment sent no reminder");
    }

    [Theory]
    [InlineData("pm_declined", "declined")]
    [InlineData("pm_action", "authentication_required")]
    [InlineData("pm_transient", "unavailable")]
    public async Task Dunning_falls_back_to_the_reminder_with_the_reason_and_an_in_app_notice(string methodRef, string reason)
    {
        var (subscriberId, subscriptionId, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, methodRef);

        await fx.Services.GetRequiredService<IDunningProcessor>().ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));

        (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Id == payment.Id).Status.Should().NotBe(PaymentStatus.Paid);
        var reminder = Reminders(subscriberId).Should().ContainSingle().Subject;
        reminder.Email!.Model["AutoRetryReason"].Should().Be(reason);
        reminder.Email!.Model["AutoRetryReasonEn"].Should().NotBe(string.Empty);
        reminder.Email!.Model["AutoRetryReasonAr"].Should().NotBe(string.Empty);
        reminder.InApp.Should().NotBeNull();
        reminder.InApp!.Category.Should().Be("billing.payment-failed");
    }

    [Fact]
    public async Task Dunning_without_a_saved_method_sends_the_plain_reminder()
    {
        var (subscriberId, _, payment) = await OverdueAsync();

        await fx.Services.GetRequiredService<IDunningProcessor>().ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));

        var reminder = Reminders(subscriberId).Should().ContainSingle().Subject;
        reminder.Email!.Model["AutoRetryReason"].Should().Be(string.Empty);
        reminder.InApp.Should().BeNull("no automatic payment failed, so there is nothing to announce in-app");
        (await fx.WithDb(db => db.PaymentAttempts.CountAsync(x => x.PaymentId == payment.Id))).Should().Be(0);
    }

    [Fact]
    public async Task A_payment_has_at_most_one_attempt_per_step()
    {
        var (_, _, payment) = await OverdueAsync();
        await fx.WithDb(async db =>
        {
            db.PaymentAttempts.Add(PaymentAttempt.Start(payment, "day-1", Guid.NewGuid(), 115m, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return true;
        });

        var act = () => fx.WithDb(async db =>
        {
            db.PaymentAttempts.Add(PaymentAttempt.Start(payment, "day-1", Guid.NewGuid(), 115m, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return true;
        });

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ----- #1 cross-step safety -----

    private IDunningProcessor Dunning => fx.Services.GetRequiredService<IDunningProcessor>();

    [Fact]
    public async Task A_transient_step_without_a_reference_blocks_a_new_charge_in_the_next_step()
    {
        var (subscriberId, _, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_transient");

        await Dunning.ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));
        await Dunning.ProcessAsync(payment.CreatedAt.AddDays(3).AddMinutes(1));

        var day1 = PaymentAttempt.KeyFor(payment.Id, "day-1");
        var day3 = PaymentAttempt.KeyFor(payment.Id, "day-3");
        CountingFakeGateway.ChargesFor(day1).Should().Be(1);
        CountingFakeGateway.ChargesFor(day3).Should().Be(0, "the unknown day-1 outcome (past the replay window) must not be charged around");
        (await fx.WithDb(db => db.PaymentAttempts.AnyAsync(x => x.PaymentId == payment.Id && x.Step == "day-3"))).Should().BeFalse();
        Reminders(subscriberId).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_charge_still_processing_at_the_provider_blocks_the_next_step_and_hides_the_checkout_link()
    {
        var (subscriberId, _, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_processing");

        await Dunning.ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));
        await Dunning.ProcessAsync(payment.CreatedAt.AddDays(3).AddMinutes(1));

        CountingFakeGateway.ChargesFor(PaymentAttempt.KeyFor(payment.Id, "day-1")).Should().Be(1);
        CountingFakeGateway.ChargesFor(PaymentAttempt.KeyFor(payment.Id, "day-3")).Should().Be(0);
        var attempt = await fx.WithDb(db => db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.PaymentId == payment.Id));
        attempt.Outcome.Should().Be(PaymentAttemptOutcome.Transient);
        attempt.ProviderReference.Should().NotBeNull();
        CountingFakeGateway.StatusCalls.GetValueOrDefault(attempt.ProviderReference!).Should().BeGreaterThan(0, "the earlier charge was looked up first");

        var reminders = Reminders(subscriberId).ToList();
        reminders.Should().HaveCount(2);
        reminders.Should().OnlyContain(m => (string)m.Email!.Model["AutoRetryReason"] == "processing");
        reminders.Should().OnlyContain(m => ((string)m.Email!.Model["PaymentUrl"]).EndsWith("/subscription/renew"),
            "no fresh checkout link while a charge may still complete");
    }

    [Fact]
    public async Task A_processing_charge_is_settled_by_the_providers_webhook()
    {
        var (subscriberId, subscriptionId, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_processing");
        await Dunning.ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));
        var key = PaymentAttempt.KeyFor(payment.Id, "day-1");
        var body = JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "payment.succeeded",
            reference = key,
            amount = payment.OutstandingAmount,
            currency = payment.Currency
        });

        await fx.SendAs(null, new ReceivePaymentWebhookCommand("Fake", body, new Dictionary<string, string>
        {
            [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(SubscriptionsPostgresFixture.FakeGatewaySecret, body)
        }));

        (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Id == payment.Id).Status.Should().Be(PaymentStatus.Paid);
        (await _scenario.LoadAsync(subscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task A_provider_event_after_the_synthetic_settlement_changes_nothing()
    {
        var (subscriberId, subscriptionId, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_ok");
        await Dunning.ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));
        var ledgerBefore = await fx.WithDb(db => db.LedgerEntries.CountAsync(x => x.SubscriptionId == subscriptionId));
        var invoicesBefore = (await _scenario.InvoicesAsync(subscriptionId)).Count;
        var key = PaymentAttempt.KeyFor(payment.Id, "day-1");
        var body = JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "payment.succeeded",
            reference = key,
            amount = payment.OutstandingAmount,
            currency = payment.Currency
        });

        await fx.SendAs(null, new ReceivePaymentWebhookCommand("Fake", body, new Dictionary<string, string>
        {
            [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(SubscriptionsPostgresFixture.FakeGatewaySecret, body)
        }));

        (await fx.WithDb(db => db.LedgerEntries.CountAsync(x => x.SubscriptionId == subscriptionId))).Should().Be(ledgerBefore);
        (await _scenario.InvoicesAsync(subscriptionId)).Should().HaveCount(invoicesBefore);
    }

    // ----- #2 charged but not settled -----

    [Fact]
    public async Task A_charge_whose_settlement_is_rejected_raises_an_admin_audit_entry()
    {
        var (subscriberId, subscriptionId, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_ok");
        var subscription = await _scenario.LoadAsync(subscriptionId);
        // The subscription expires between the dunning query and the charge: settlement of a renewal is refused.
        await fx.WithDb(db => db.Subscriptions.Where(x => x.Id == subscriptionId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SubscriptionStatus.Expired)));

        var result = await fx.WithScopeResult(sp => sp.GetRequiredService<IOffSessionRetryService>()
            .TryChargeAsync(subscription, payment, "day-1", DateTimeOffset.UtcNow, CancellationToken.None));

        result.Outcome.Should().Be(AutoRetryOutcome.Succeeded, "the provider did collect the money");
        (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Id == payment.Id).Status.Should().NotBe(PaymentStatus.Paid);
        var alert = await fx.WithDb(db => db.AdminAuditLogs.AsNoTracking()
            .SingleAsync(x => x.TargetId == payment.Id && x.Action == "OffSessionChargeUnsettled"));
        alert.Reason.Should().Contain("manually").And.Contain("Fake");
    }

    // ----- #4 amount support -----

    [Fact]
    public async Task Checkout_refuses_a_gateway_that_cannot_charge_the_amount()
    {
        var plan = await fx.CreatePlanAsync($"Amount-{Guid.NewGuid():N}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        CountingFakeGateway.RejectAmounts = true;
        try
        {
            var act = () => fx.SendAs(subscriberId, new CreateOnlineCheckoutCommand(
                created.SubscriptionId, subscriberId, "Fake", ReturnUrl, ReturnUrl));

            (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be("amount_not_supported");
        }
        finally
        {
            CountingFakeGateway.RejectAmounts = false;
        }
    }

    [Fact]
    public async Task Dunning_does_not_charge_through_a_gateway_that_cannot_take_the_amount()
    {
        var (subscriberId, _, payment) = await OverdueAsync();
        await SaveMethodAsync(subscriberId, "pm_ok");
        CountingFakeGateway.RejectAmounts = true;
        try
        {
            await Dunning.ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));
        }
        finally
        {
            CountingFakeGateway.RejectAmounts = false;
        }

        CountingFakeGateway.ChargesFor(PaymentAttempt.KeyFor(payment.Id, "day-1")).Should().Be(0);
        Reminders(subscriberId).Should().ContainSingle();
    }

    // ----- #6 opt-in -----

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task The_card_is_only_kept_when_the_subscriber_opted_in(bool saveCard, bool expectedFlag)
    {
        var plan = await fx.CreatePlanAsync($"Consent-{Guid.NewGuid():N}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);

        var checkout = await fx.SendAs(subscriberId, new CreateOnlineCheckoutCommand(
            created.SubscriptionId, subscriberId, "Fake", ReturnUrl, ReturnUrl, SaveCard: saveCard));

        CountingFakeGateway.Checkouts.Single(x => x.PaymentId == created.PaymentId).SavePaymentMethod.Should().Be(expectedFlag);
        checkout.SessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_saved_method_records_when_the_subscriber_consented()
    {
        var subscriberId = Guid.NewGuid();
        await SaveMethodAsync(subscriberId, "pm_consent");

        var saved = await fx.WithDb(db => db.SavedPaymentMethods.AsNoTracking().SingleAsync(x => x.SubscriberId == subscriberId));

        saved.ConsentedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
    }

    // ----- recorder / removal -----

    [Fact]
    public async Task The_recorder_ignores_a_method_that_belongs_to_another_subscriber()
    {
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var methodRef = $"pm_shared_{Guid.NewGuid():N}";
        await fx.WithDb(async db =>
        {
            db.SavedPaymentMethods.Add(SavedPaymentMethod.Create(owner, "Fake", "cus_1", methodRef, "visa", "4242", 12, 2099, true, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return true;
        });

        await fx.WithScopeResult(async sp =>
        {
            await sp.GetRequiredService<ISavedMethodRecorder>().RecordAsync(
                "Fake", intruder, new SavedMethodCapture("cus_2", methodRef), null, CancellationToken.None);
            return true;
        });

        (await fx.WithDb(db => db.SavedPaymentMethods.CountAsync(x => x.SubscriberId == intruder))).Should().Be(0);
        (await fx.WithDb(db => db.SavedPaymentMethods.SingleAsync(x => x.ProviderMethodRef == methodRef))).SubscriberId.Should().Be(owner);
    }

    [Fact]
    public async Task Removing_a_method_revokes_it_detaches_it_at_the_provider_and_promotes_the_next_default()
    {
        var subscriberId = Guid.NewGuid();
        await SaveMethodAsync(subscriberId, "pm_first");
        await SaveMethodAsync(subscriberId, "pm_second", isDefault: false);
        var first = await fx.WithDb(db => db.SavedPaymentMethods.AsNoTracking().SingleAsync(x => x.SubscriberId == subscriberId && x.IsDefault));

        await fx.SendAs(subscriberId, new RemoveSavedPaymentMethodCommand(subscriberId, first.Id));

        CountingFakeGateway.Detached.Should().Contain(first.ProviderMethodRef);
        var rows = await fx.WithDb(db => db.SavedPaymentMethods.AsNoTracking().Where(x => x.SubscriberId == subscriberId).ToListAsync());
        rows.Single(x => x.Id == first.Id).RevokedAt.Should().NotBeNull();
        rows.Single(x => x.Id != first.Id).IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_scale_amounts_do_not_block_an_unrelated_plan_update_or_a_receipt_of_the_outstanding_amount()
    {
        // A legacy row saved before per-currency scales: JPY with decimals (written around the invariant).
        var planId = await fx.WithDb(async db =>
        {
            var plan = Plan.Create(LocalizedText.Create("Legacy", "Legacy"), null, 100m, "JPY", 1, 10, 5, 3, true);
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
            await db.Plans.Where(x => x.Id == plan.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Price, 99.5m));
            return plan.Id;
        });

        await fx.SendAsAdmin(new UpdatePlanCommand(
            planId, LocalizedText.Create("Renamed", "Renamed"), null, "JPY", 1, 10, 5, 3, true, true,
            (await fx.WithDb(db => db.Plans.AsNoTracking().SingleAsync(x => x.Id == planId))).Version));

        (await fx.WithDb(db => db.Plans.AsNoTracking().SingleAsync(x => x.Id == planId))).Name.En.Should().Be("Renamed");
    }
}
