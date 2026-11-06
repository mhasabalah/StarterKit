using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Data;
using Subscriptions.Domains.OnlinePayments.Features.ReceivePaymentWebhook;
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
                subscriberId, "Fake", "cus_test", methodRef, "visa", "4242", 12, 2099, isDefault, DateTimeOffset.UtcNow));
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
        (await fx.WithDb(db => db.PaymentAttempts.CountAsync(x => x.PaymentId == payment.Id)))
            .Should().Be(1, "a step is attempted once; a re-run never charges again");
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
        reminder.InApp.Should().NotBeNull();
        reminder.InApp!.Category.Should().Be("billing.payment-failed");
    }

    [Fact]
    public async Task Dunning_without_a_saved_method_sends_the_plain_reminder()
    {
        var (subscriberId, _, payment) = await OverdueAsync();

        await fx.Services.GetRequiredService<IDunningProcessor>().ProcessAsync(payment.CreatedAt.AddDays(1).AddMinutes(1));

        Reminders(subscriberId).Should().ContainSingle().Which.Email!.Model["AutoRetryReason"].Should().Be(string.Empty);
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
}
