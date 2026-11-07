using System.Globalization;
using System.Resources;
using FluentAssertions;
using Moq;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Contracts.Mediation;
using Shared.Core.MultiTenancy;
using Attachment.Contracts.Attachments.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Events;
using Subscriptions.Domains.Subscriptions.EventHandlers;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Jobs;
using Subscriptions.Domains.Subscriptions.Models;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Resources;
using Subscriptions.Tests.TestSupport;
using Xunit;

namespace Subscriptions.Tests;

/// <summary>
/// Per-sender wiring of the billing notifications: for each of the 16 event handlers, <c>InvoiceDeliveryJob</c> and
/// <c>TrialReminderProcessor</c> the email is still sent, the in-app entry has the exact <c>billing.&lt;event&gt;</c>
/// category with resx-backed en/ar text, a push is attached exactly for the actionable events, and the send runs inside
/// the subscriber's tenant scope with <c>tenantId</c> in the client payload.
/// </summary>
public class BillingNotificationWiringTests
{
    /// <summary>One sender under test. <see cref="Push"/>: null = no push, otherwise the expected <c>sensitive</c> flag.</summary>
    private sealed record Case(
        string Key, EmailTemplate Template, bool? Push,
        Func<BillingNotificationHarness, Task> Run);

    private static Case Handler<THandler, TEvent>(
        string key, EmailTemplate template, Func<Guid, Guid, TEvent> evt, bool? push = null)
        where TEvent : INotification
        where THandler : class, INotificationHandler<TEvent> =>
        new(key, template, push,
            h => h.Create<THandler>().Handle(evt(h.SubscriptionId, h.SubscriberId), CancellationToken.None));

    private static readonly DateTimeOffset Now = new(2026, 1, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMonths(1);

    private static readonly Case[] Cases =
    [
        Handler<SubscriptionActivatedEventHandler, SubscriptionActivatedEvent>(
            "subscription-activated", EmailTemplate.SubscriptionActivated, (s, u) => new(s, u, Now, Later)),
        Handler<SubscriptionPastDueEventHandler, SubscriptionPastDueEvent>(
            "past-due", EmailTemplate.SubscriptionPastDue, (s, u) => new(s, u, Later, Guid.NewGuid()), push: true),
        Handler<SubscriptionExpiredEventHandler, SubscriptionExpiredEvent>(
            "expired", EmailTemplate.SubscriptionExpired, (s, u) => new(s, u), push: false),
        Handler<SubscriptionRenewedEventHandler, SubscriptionRenewedEvent>(
            "renewed", EmailTemplate.SubscriptionRenewed, (s, u) => new(s, u, Later)),
        Handler<PaymentRejectedEventHandler, PaymentRejectedEvent>(
            "payment-rejected", EmailTemplate.PaymentRejected, (s, u) => new(Guid.NewGuid(), s, u, "blurry proof"), push: true),
        Handler<SubscriptionRejectedEventHandler, SubscriptionRejectedEvent>(
            "subscription-rejected", EmailTemplate.SubscriptionRejected, (s, u) => new(s, u, "no"), push: false),
        Handler<RefundApprovedEventHandler, RefundApprovedEvent>(
            "refund-approved", EmailTemplate.RefundApproved, (s, u) => new(Guid.NewGuid(), s, u, 10m, "SAR")),
        Handler<RefundProcessedEventHandler, RefundProcessedEvent>(
            "refund-processed", EmailTemplate.RefundProcessed, (s, u) => new(Guid.NewGuid(), s, u, 10m, "SAR", "TX-1"),
            push: true),
        Handler<RefundRejectedEventHandler, RefundRejectedEvent>(
            "refund-rejected", EmailTemplate.RefundRejected, (s, u) => new(Guid.NewGuid(), s, u, "policy")),
        Handler<SubscriptionCancellationScheduledEventHandler, SubscriptionCancellationScheduledEvent>(
            "cancellation-scheduled", EmailTemplate.SubscriptionCancellationScheduled, (s, u) => new(s, u, u, Later, null)),
        Handler<SubscriptionCancelledEventHandler, SubscriptionCancelledEvent>(
            "cancelled", EmailTemplate.SubscriptionCancelled, (s, u) => new(s, u, Now, u)),
        Handler<SubscriptionDowngradeScheduledEventHandler, SubscriptionDowngradeScheduledEvent>(
            "downgrade-scheduled", EmailTemplate.SubscriptionDowngradeScheduled, (s, u) => new(s, u, Guid.NewGuid(), Later)),
        Handler<SubscriptionDowngradeCancelledEventHandler, SubscriptionDowngradeCancelledEvent>(
            "downgrade-cancelled", EmailTemplate.SubscriptionDowngradeCancelled, (s, u) => new(s, u)),
        Handler<SubscriptionDowngradeAppliedEventHandler, SubscriptionDowngradeAppliedEvent>(
            "downgrade-applied", EmailTemplate.SubscriptionDowngradeApplied, (s, u) => new(s, u, Guid.NewGuid())),
        Handler<SubscriptionUpgradedEventHandler, SubscriptionUpgradedEvent>(
            "upgraded", EmailTemplate.SubscriptionUpgraded, (s, u) => new(s, u, Guid.NewGuid(), Guid.NewGuid(), 5m, false)),
        Handler<SubscriptionUpgradeRevertedEventHandler, SubscriptionUpgradeRevertedEvent>(
            "upgrade-reverted", EmailTemplate.SubscriptionUpgradeReverted, (s, u) => new(s, u, Guid.NewGuid()), push: false),

        new("invoice-ready", EmailTemplate.InvoiceIssued, null, RunInvoiceDeliveryJob),
        new("trial-ending", EmailTemplate.TrialEnding, false, RunTrialReminderProcessor)
    ];

    public static IEnumerable<object[]> Keys() => Cases.Select(c => new object[] { c.Key });

    private static async Task RunInvoiceDeliveryJob(BillingNotificationHarness h)
    {
        PdfConfiguration.Configure();
        var invoice = SubscriptionInvoice.Issue(
            new InvoiceDraft(
                h.SubscriptionId, h.SubscriberId, Guid.NewGuid(), InvoiceType.Activation, "Activation - Pro",
                Guid.NewGuid(), "Pro", 1, Now, Later,
                new InvoiceParty("Buyer", null, null, null),
                new InvoiceParty("Seller", null, null, null),
                TaxCalculator.FromGross(115m, SubscriptionFactory.Tax, "SAR"), "SAR"),
            "INV-2026-000001", Now);
        h.DbContext.Invoices.Add(invoice);
        await h.DbContext.SaveChangesAsync();

        h.Sender.Setup(s => s.Send(It.IsAny<UploadAttachmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadAttachmentResult(Guid.NewGuid(), "INV.pdf", "/f", null, 1, "application/pdf"));

        await h.Create<InvoiceDeliveryJob>().DeliverAsync(invoice.Id, forceResend: false);
    }

    private static async Task RunTrialReminderProcessor(BillingNotificationHarness h)
    {
        var trial = Subscription.StartTrial(h.SubscriberId, SubscriptionFactory.Plan(100m), 14, Now);
        h.DbContext.Subscriptions.Add(trial);
        await h.DbContext.SaveChangesAsync();
        h.Subscriptions.Setup(r => r.GetByIdAsync(trial.Id, It.IsAny<CancellationToken>())).ReturnsAsync(trial);

        var sent = await h.Create<TrialReminderProcessor>().ProcessAsync(trial.TrialEndsAt!.Value.AddDays(-1));
        sent.Should().Be(1);
    }

    private static Case Find(string key) => Cases.Single(c => c.Key == key);

    private static async Task<(RecordedSend Send, BillingNotificationHarness Harness)> RunAsync(string key)
    {
        var harness = new BillingNotificationHarness();
        await Find(key).Run(harness);
        return (harness.Notifications.Sends.Should().ContainSingle().Subject, harness);
    }

    [Fact]
    public void Every_billing_sender_is_covered()
    {
        Cases.Should().HaveCount(18);
        Cases.Select(c => c.Key).Should().OnlyHaveUniqueItems();
        Cases.Count(c => c.Push is not null).Should().Be(7, "past-due, payment rejected, refund processed, expired, rejected, upgrade reverted, trial ending");
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Sender_still_sends_its_email_once_on_the_email_channel_to_the_subscriber(string key)
    {
        var (send, harness) = await RunAsync(key);

        send.Emails.Should().Equal(Find(key).Template);
        send.SendCount.Should().Be(1);
        send.ForcedChannel.Should().Be(NotificationChannel.Email);
        send.UserId.Should().Be(harness.SubscriberId);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Sender_attaches_the_billing_in_app_entry_with_resx_text_in_both_languages(string key)
    {
        var (send, _) = await RunAsync(key);

        var content = send.InApps.Should().ContainSingle().Subject;
        content.Category.Should().Be($"billing.{key}");

        var resources = new ResourceManager(typeof(SubscriptionsResource));
        foreach (var (part, text) in new[] { ("Title", content.Title), ("Body", content.Body) })
        {
            var resxKey = $"InApp.billing.{key}.{part}";
            foreach (var (culture, actual) in new[] { ("en", text.En), ("ar", text.Ar) })
            {
                var template = resources.GetString(resxKey, CultureInfo.GetCultureInfo(culture));
                template.Should().NotBeNullOrWhiteSpace($"resx key {resxKey} must exist for {culture}");
                actual.Should().NotBeNullOrWhiteSpace().And.NotStartWith("InApp.").And.NotContain("{");
                var brace = template!.IndexOf('{');
                actual.Should().StartWith(brace >= 0 ? template[..brace] : template,
                    "the text is the resx template with its arguments filled in");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Sender_pushes_only_when_the_event_is_actionable(string key)
    {
        var (send, _) = await RunAsync(key);

        var expected = Find(key).Push;
        if (expected is null)
            send.Pushes.Should().BeEmpty();
        else
            send.Pushes.Should().Equal([expected.Value], "exactly one push, with the sensitivity the event calls for");
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task Sender_sends_inside_the_subscribers_tenant_scope_and_carries_tenant_id(string key)
    {
        var (send, harness) = await RunAsync(key);

        send.TenantDuringSend.Should().Be(harness.Tenant);
        TenantScope.Current?.Tenant.Should().NotBe(harness.Tenant, "the scope is left after the send");

        send.InApps.Single().Data.Should().Contain("tenantId", harness.SubscriberId.ToString());
    }

    [Fact]
    public async Task Cancellation_applied_by_the_period_end_job_sends_nothing_again()
    {
        var harness = new BillingNotificationHarness();

        await harness.Create<SubscriptionCancelledEventHandler>().Handle(
            new SubscriptionCancelledEvent(harness.SubscriptionId, harness.SubscriberId, Now, harness.SubscriberId, null, IsScheduled: true),
            CancellationToken.None);

        harness.Notifications.Sends.Should().BeEmpty("the email (and inbox entry) went out when the cancellation was scheduled");
    }
}
