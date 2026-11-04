using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Ledger.Enums;
using Subscriptions.Data;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Features.ReceivePaymentWebhook;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Jobs;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Subscriptions.Models;
using SubscriptionStatus = Subscriptions.Contracts.Subscriptions.Enums.SubscriptionStatus;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// Online payments against real PostgreSQL through the real pipeline: checkout, signed webhook, settlement through
/// the shared settlement path, plus every refusal (bad signature, duplicate, unknown reference/event, amount mismatch,
/// already settled) and the retry job.
/// </summary>
[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class OnlinePaymentTests(SubscriptionsPostgresFixture fx)
{
    private const string Fake = "Fake";
    private const string ReturnUrl = "https://app.example.com/ok";

    private readonly Scenario _scenario = new(fx);

    private sealed record Checkout(Guid SubscriberId, Guid SubscriptionId, Guid PaymentId, string SessionId, decimal Amount);

    private async Task<Checkout> StartAsync()
    {
        var plan = await fx.CreatePlanAsync($"Online-{Guid.NewGuid():N}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        var checkout = await fx.SendAs(subscriberId, new CreateOnlineCheckoutCommand(
            created.SubscriptionId, subscriberId, Fake, ReturnUrl, ReturnUrl));
        return new Checkout(subscriberId, created.SubscriptionId, created.PaymentId, checkout.SessionId, checkout.Amount);
    }

    private static string Body(string eventId, string type, string reference, decimal amount, string currency = "SAR") =>
        JsonSerializer.Serialize(new { id = eventId, type, reference, amount, currency });

    private Task<ReceivePaymentWebhookResult> SendWebhookAsync(string body, string? signature = null, string provider = Fake) =>
        fx.SendAs(null, new ReceivePaymentWebhookCommand(provider, body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakePaymentGateway.SignatureHeader] = signature ?? FakePaymentGateway.Sign(SubscriptionsPostgresFixture.FakeGatewaySecret, body)
        }));

    private Task<InboundWebhookEvent> InboxAsync(string eventId) =>
        fx.WithDb(db => db.InboundWebhookEvents.AsNoTracking().SingleAsync(x => x.Provider == Fake && x.EventId == eventId));

    private Task<int> InboxCountAsync(string eventId) =>
        fx.WithDb(db => db.InboundWebhookEvents.CountAsync(x => x.EventId == eventId));

    private Task<int> LedgerCountAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.LedgerEntries.CountAsync(x => x.SubscriptionId == subscriptionId));

    private Task<OnlinePaymentSession> SessionAsync(string sessionId) =>
        fx.WithDb(db => db.OnlinePaymentSessions.AsNoTracking().SingleAsync(x => x.ProviderReference == sessionId));

    private static string EventId() => $"evt_{Guid.NewGuid():N}";

    // ----- checkout -----

    [Fact]
    public async Task Checkout_records_a_pending_session_for_the_outstanding_amount()
    {
        var c = await StartAsync();

        var session = await SessionAsync(c.SessionId);
        session.PaymentId.Should().Be(c.PaymentId);
        session.Provider.Should().Be(Fake);
        session.Status.Should().Be(OnlinePaymentSessionStatus.Created);
        session.Amount.Should().Be(c.Amount).And.BeGreaterThan(0m);
    }

    [Fact]
    public async Task Checkout_for_another_subscribers_subscription_is_not_found()
    {
        var c = await StartAsync();
        var stranger = Guid.NewGuid();

        var act = () => fx.SendAs(stranger, new CreateOnlineCheckoutCommand(
            c.SubscriptionId, stranger, Fake, ReturnUrl, ReturnUrl));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Checkout_with_an_unknown_or_disabled_provider_is_not_found()
    {
        var c = await StartAsync();

        var act = () => fx.SendAs(c.SubscriberId, new CreateOnlineCheckoutCommand(
            c.SubscriptionId, c.SubscriberId, "Nope", ReturnUrl, ReturnUrl));

        await act.Should().ThrowAsync<PaymentGatewayNotFoundException>();
    }

    [Fact]
    public async Task Checkout_with_a_relative_return_url_is_rejected()
    {
        var c = await StartAsync();

        var act = () => fx.SendAs(c.SubscriberId, new CreateOnlineCheckoutCommand(
            c.SubscriptionId, c.SubscriberId, Fake, "/ok", ReturnUrl));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    // ----- success -----

    [Fact]
    public async Task Success_webhook_activates_the_subscription_with_invoice_and_ledger_entry()
    {
        var c = await StartAsync();
        var id = EventId();

        var result = await SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount));

        result.Duplicate.Should().BeFalse();
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
        var payment = (await _scenario.PaymentsAsync(c.SubscriptionId)).Single();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.AmountReceived.Should().Be(c.Amount);
        (await _scenario.InvoicesAsync(c.SubscriptionId)).Should().ContainSingle();

        var ledger = await fx.WithDb(db => db.LedgerEntries.AsNoTracking().Where(x => x.SubscriptionId == c.SubscriptionId).ToListAsync());
        ledger.Should().ContainSingle(x => x.Type == LedgerEntryType.Payment && x.Amount == c.Amount && x.Reference == c.SessionId);

        var inbox = await InboxAsync(id);
        inbox.Status.Should().Be(InboundWebhookStatus.Processed);
        inbox.ProcessedAt.Should().NotBeNull();
        (await SessionAsync(c.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Succeeded);
    }

    [Fact]
    public async Task Duplicate_event_is_acknowledged_and_never_reprocessed()
    {
        var c = await StartAsync();
        var id = EventId();
        var body = Body(id, "payment.succeeded", c.SessionId, c.Amount);

        (await SendWebhookAsync(body)).Duplicate.Should().BeFalse();
        (await SendWebhookAsync(body)).Duplicate.Should().BeTrue();

        (await InboxCountAsync(id)).Should().Be(1);
        (await LedgerCountAsync(c.SubscriptionId)).Should().Be(1);
        (await _scenario.InvoicesAsync(c.SubscriptionId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Same_session_reported_under_a_new_event_id_settles_once()
    {
        var c = await StartAsync();

        await SendWebhookAsync(Body(EventId(), "payment.succeeded", c.SessionId, c.Amount));
        var second = EventId();
        await SendWebhookAsync(Body(second, "payment.succeeded", c.SessionId, c.Amount));

        (await InboxAsync(second)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await LedgerCountAsync(c.SubscriptionId)).Should().Be(1);
    }

    // ----- refusals -----

    [Fact]
    public async Task Bad_signature_is_refused_and_nothing_is_stored()
    {
        var c = await StartAsync();
        var id = EventId();

        var act = () => SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount), signature: "deadbeef");

        await act.Should().ThrowAsync<InvalidWebhookSignatureException>();
        (await InboxCountAsync(id)).Should().Be(0);
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.PendingApproval);
    }

    [Fact]
    public async Task Signature_over_a_different_body_is_refused()
    {
        var c = await StartAsync();
        var id = EventId();
        var signedForOther = FakePaymentGateway.Sign(
            SubscriptionsPostgresFixture.FakeGatewaySecret, Body(id, "payment.succeeded", c.SessionId, 1m));

        var act = () => SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount), signedForOther);

        await act.Should().ThrowAsync<InvalidWebhookSignatureException>();
        (await InboxCountAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task Signed_but_malformed_body_is_a_bad_request()
    {
        var act = () => SendWebhookAsync("{ \"nope\": true }");

        await act.Should().ThrowAsync<InvalidWebhookPayloadException>();
    }

    [Fact]
    public async Task Webhook_for_an_unknown_provider_is_not_found()
    {
        var act = () => SendWebhookAsync(Body(EventId(), "payment.succeeded", "x", 1m), provider: "Nope");

        await act.Should().ThrowAsync<PaymentGatewayNotFoundException>();
    }

    [Fact]
    public async Task Unknown_payment_reference_is_stored_as_a_permanent_failure_and_listed_for_admins()
    {
        var id = EventId();

        (await SendWebhookAsync(Body(id, "payment.succeeded", "fake_unknown", 10m))).Duplicate.Should().BeFalse();

        var inbox = await InboxAsync(id);
        inbox.Status.Should().Be(InboundWebhookStatus.Failed);
        inbox.Attempts.Should().BeGreaterThanOrEqualTo(5, "a permanent rejection exhausts the attempts at once");
        inbox.LastError.Should().Contain("Unknown payment reference");

        var failed = await fx.SendAsAdmin(new GetFailedWebhookEventsQuery { Provider = Fake, PageSize = 100 });
        failed.Result!.Should().Contain(x => x.EventId == id);
    }

    [Fact]
    public async Task Amount_mismatch_is_stored_as_failed_and_nothing_is_settled()
    {
        var c = await StartAsync();
        var id = EventId();

        await SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount - 1m));

        var inbox = await InboxAsync(id);
        inbox.Status.Should().Be(InboundWebhookStatus.Failed);
        inbox.LastError.Should().Contain("mismatch");
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.PendingApproval);
        (await _scenario.PaymentsAsync(c.SubscriptionId)).Single().Status.Should().Be(PaymentStatus.Pending);
        (await LedgerCountAsync(c.SubscriptionId)).Should().Be(0);
    }

    [Fact]
    public async Task Currency_mismatch_is_stored_as_failed()
    {
        var c = await StartAsync();
        var id = EventId();

        await SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount, "USD"));

        (await InboxAsync(id)).Status.Should().Be(InboundWebhookStatus.Failed);
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.PendingApproval);
    }

    [Fact]
    public async Task Payment_already_settled_by_an_admin_is_not_booked_twice()
    {
        var c = await StartAsync();
        await fx.SendAsAdmin(new RecordManualPaymentCommand(
            c.SubscriptionId, TestIds.Admin, c.Amount, "Cash", PaymentId: c.PaymentId));
        var id = EventId();

        await SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount));

        var inbox = await InboxAsync(id);
        inbox.Status.Should().Be(InboundWebhookStatus.Failed);
        inbox.LastError.Should().Contain("already");
        (await LedgerCountAsync(c.SubscriptionId)).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_event_type_is_stored_and_marked_processed_without_effect()
    {
        var c = await StartAsync();
        var id = EventId();

        await SendWebhookAsync(Body(id, "customer.updated", c.SessionId, c.Amount));

        (await InboxAsync(id)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.PendingApproval);
    }

    [Fact]
    public async Task Failed_payment_event_marks_the_session_failed_and_leaves_the_payment_owed()
    {
        var c = await StartAsync();

        await SendWebhookAsync(Body(EventId(), "payment.failed", c.SessionId, c.Amount));

        (await SessionAsync(c.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Failed);
        (await _scenario.PaymentsAsync(c.SubscriptionId)).Single().Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Refund_event_marks_a_settled_session_refunded()
    {
        var c = await StartAsync();
        await SendWebhookAsync(Body(EventId(), "payment.succeeded", c.SessionId, c.Amount));

        await SendWebhookAsync(Body(EventId(), "refund.created", c.SessionId, c.Amount));

        (await SessionAsync(c.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Refunded);
    }

    // ----- retry job -----

    [Fact]
    public async Task Retry_job_processes_a_transiently_failed_event_and_skips_exhausted_ones()
    {
        var c = await StartAsync();
        var retryable = EventId();
        var exhausted = EventId();
        var past = DateTimeOffset.UtcNow.AddHours(-1);

        // Rows as the processor leaves them after a transient failure (1 attempt) and an exhausted one (5 attempts).
        await fx.WithScope(async services =>
        {
            var db = services.GetRequiredService<SubscriptionsDbContext>();
            foreach (var (eventId, attempts) in new[] { (retryable, 1), (exhausted, 5) })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO subscriptions.inbound_webhook_events
                        (id, provider, event_id, type, provider_reference, amount, currency, payload, status, received_at,
                         last_attempt_at, attempts, last_error)
                    VALUES ({Guid.NewGuid()}, {Fake}, {eventId}, {"PaymentSucceeded"}, {c.SessionId}, {c.Amount}, {"SAR"},
                            {"{}"}::jsonb, {"Failed"}, {past}, {past}, {attempts}, {"boom"})
                    """);
            }
        });

        await fx.WithScope(services =>
            services.GetRequiredService<RetryFailedPaymentWebhooksJob>().ExecuteAsync(new JobCancellationToken(false)));

        (await InboxAsync(retryable)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await InboxAsync(exhausted)).Status.Should().Be(InboundWebhookStatus.Failed, "attempts are bounded");
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
    }

    // ----- abandoned Pending rows -----

    /// <summary>A row as left by a request that was cancelled or crashed between the insert and processing.</summary>
    private Task InsertPendingAsync(Checkout c, string eventId, DateTimeOffset receivedAt) =>
        fx.WithScope(async services =>
        {
            var db = services.GetRequiredService<SubscriptionsDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO subscriptions.inbound_webhook_events
                    (id, provider, event_id, type, provider_reference, amount, currency, payload, status, received_at, attempts)
                VALUES ({Guid.NewGuid()}, {Fake}, {eventId}, {"PaymentSucceeded"}, {c.SessionId}, {c.Amount}, {"SAR"},
                        {"{}"}::jsonb, {"Pending"}, {receivedAt}, 0)
                """);
        });

    [Fact]
    public async Task Duplicate_delivery_of_an_unprocessed_pending_row_processes_it()
    {
        var c = await StartAsync();
        var id = EventId();
        await InsertPendingAsync(c, id, DateTimeOffset.UtcNow);

        var result = await SendWebhookAsync(Body(id, "payment.succeeded", c.SessionId, c.Amount));

        result.Duplicate.Should().BeTrue();
        (await InboxAsync(id)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
        (await LedgerCountAsync(c.SubscriptionId)).Should().Be(1);
    }

    [Fact]
    public async Task Retry_job_processes_a_pending_row_abandoned_for_minutes_but_not_a_fresh_one()
    {
        var c = await StartAsync();
        var abandoned = EventId();
        await InsertPendingAsync(c, abandoned, DateTimeOffset.UtcNow.AddMinutes(-30));

        var other = await StartAsync();
        var fresh = EventId();
        await InsertPendingAsync(other, fresh, DateTimeOffset.UtcNow);

        await fx.WithScope(services =>
            services.GetRequiredService<RetryFailedPaymentWebhooksJob>().ExecuteAsync(new JobCancellationToken(false)));

        (await InboxAsync(abandoned)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await _scenario.LoadAsync(c.SubscriptionId)).Status.Should().Be(SubscriptionStatus.Active);
        (await InboxAsync(fresh)).Status.Should().Be(InboundWebhookStatus.Pending, "it may still be processed inline");
    }
}
