using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Ledger.Enums;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Features.ReceivePaymentWebhook;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.Ledger.Models;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Plans.Models;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// Refunds of online payments against real PostgreSQL: a provider refund webhook reaches the ledger and the credit
/// notes exactly once per provider refund id and never beyond the money received; an admin refund of an online payment
/// goes back through the gateway and its own echo is recognized.
/// </summary>
[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class OnlineRefundTests(SubscriptionsPostgresFixture fx)
{
    private const string Fake = "Fake";
    private const string ReturnUrl = "https://app.example.com/ok";

    private readonly Scenario _scenario = new(fx);

    private sealed record Paid(Guid SubscriberId, Guid SubscriptionId, string SessionId, decimal Amount, string PaymentIntent);

    private static string EventId() => $"evt_{Guid.NewGuid():N}";

    private async Task<Paid> PaidAsync(int cycle = 1, decimal price = 100m)
    {
        var plan = await fx.CreatePlanAsync($"OnlineRefund-{Guid.NewGuid():N}", price, cycle: cycle);
        return await PaidAsync(plan);
    }

    private async Task<Paid> PaidAsync(Plan plan)
    {
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        var checkout = await fx.SendAs(subscriberId, new CreateOnlineCheckoutCommand(
            created.SubscriptionId, subscriberId, Fake, ReturnUrl, ReturnUrl));
        var intent = $"pi_{Guid.NewGuid():N}";

        await SendAsync(JsonSerializer.Serialize(new
        {
            id = EventId(), type = "payment.succeeded", reference = checkout.SessionId, amount = checkout.Amount,
            currency = "SAR", payment_reference = intent
        }));
        return new Paid(subscriberId, created.SubscriptionId, checkout.SessionId, checkout.Amount, intent);
    }

    private Task<ReceivePaymentWebhookResult> SendAsync(string body) =>
        fx.SendAs(null, new ReceivePaymentWebhookCommand(Fake, body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(SubscriptionsPostgresFixture.FakeGatewaySecret, body)
        }));

    private Task<ReceivePaymentWebhookResult> RefundWebhookAsync(
        string reference, decimal amount, string refundId, string? eventId = null, string currency = "SAR") =>
        SendAsync(JsonSerializer.Serialize(new
        {
            id = eventId ?? EventId(), type = "refund.created", reference, amount, currency, refund_id = refundId
        }));

    private Task<List<LedgerEntry>> LedgerAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.LedgerEntries.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).ToListAsync());

    private Task<int> CreditNotesAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.Invoices.CountAsync(x => x.SubscriptionId == subscriptionId && x.Type == InvoiceType.CreditNote));

    private Task<OnlinePaymentSession> SessionAsync(string sessionId) =>
        fx.WithDb(db => db.OnlinePaymentSessions.AsNoTracking().SingleAsync(x => x.ProviderReference == sessionId));

    private Task<InboundWebhookEvent> InboxAsync(string eventId) =>
        fx.WithDb(db => db.InboundWebhookEvents.AsNoTracking().SingleAsync(x => x.Provider == Fake && x.EventId == eventId));

    // ----- provider refund webhooks -----

    [Fact]
    public async Task Success_webhook_remembers_the_provider_payment_id_on_the_session()
    {
        var paid = await PaidAsync();

        (await SessionAsync(paid.SessionId)).ProviderPaymentReference.Should().Be(paid.PaymentIntent);
    }

    [Fact]
    public async Task Full_refund_webhook_books_a_ledger_refund_and_a_credit_note_and_marks_the_session_refunded()
    {
        var paid = await PaidAsync();

        await RefundWebhookAsync(paid.SessionId, paid.Amount, "re_full");

        var ledger = await LedgerAsync(paid.SubscriptionId);
        ledger.Should().ContainSingle(x => x.Type == LedgerEntryType.Payment && x.Amount == paid.Amount);
        var refund = ledger.Should().ContainSingle(x => x.Type == LedgerEntryType.Refund).Subject;
        refund.Amount.Should().Be(-paid.Amount);
        refund.Reference.Should().Be("re_full");
        refund.Currency.Should().Be("SAR");
        (await CreditNotesAsync(paid.SubscriptionId)).Should().Be(1);
        (await SessionAsync(paid.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Refunded);
        (await fx.WithDb(db => db.OnlinePaymentRefunds.AsNoTracking().SingleAsync(x => x.ProviderRefundId == "re_full")))
            .Should().Match<OnlinePaymentRefund>(x => x.Amount == paid.Amount && x.BookedByWebhook && x.RefundRequestId == null);
    }

    [Fact]
    public async Task Refund_event_may_reference_the_provider_payment_id_instead_of_the_checkout_session()
    {
        var paid = await PaidAsync();

        await RefundWebhookAsync(paid.PaymentIntent, 10m, "re_by_intent");

        (await LedgerAsync(paid.SubscriptionId)).Should().ContainSingle(x => x.Type == LedgerEntryType.Refund && x.Amount == -10m);
    }

    [Fact]
    public async Task Partial_refunds_accumulate_and_the_session_is_refunded_only_when_nothing_is_left()
    {
        var paid = await PaidAsync();
        var first = Math.Round(paid.Amount / 4m, 2);

        await RefundWebhookAsync(paid.SessionId, first, "re_p1");
        (await SessionAsync(paid.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Succeeded, "a partial refund leaves the session settled");

        await RefundWebhookAsync(paid.SessionId, paid.Amount - first, "re_p2");

        var refunds = (await LedgerAsync(paid.SubscriptionId)).Where(x => x.Type == LedgerEntryType.Refund).ToList();
        refunds.Should().HaveCount(2);
        refunds.Sum(x => x.Amount).Should().Be(-paid.Amount);
        (await SessionAsync(paid.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Refunded);
        (await CreditNotesAsync(paid.SubscriptionId)).Should().Be(2);
    }

    [Fact]
    public async Task Same_event_delivered_twice_books_the_refund_once()
    {
        var paid = await PaidAsync();
        var eventId = EventId();

        (await RefundWebhookAsync(paid.SessionId, 10m, "re_dup_evt", eventId)).Duplicate.Should().BeFalse();
        (await RefundWebhookAsync(paid.SessionId, 10m, "re_dup_evt", eventId)).Duplicate.Should().BeTrue();

        (await LedgerAsync(paid.SubscriptionId)).Count(x => x.Type == LedgerEntryType.Refund).Should().Be(1);
    }

    [Fact]
    public async Task Same_refund_reported_under_a_new_event_id_is_booked_once()
    {
        var paid = await PaidAsync();
        var second = EventId();

        await RefundWebhookAsync(paid.SessionId, 10m, "re_dup_id");
        (await RefundWebhookAsync(paid.SessionId, 10m, "re_dup_id", second)).Duplicate.Should().BeFalse("a new event id is a new inbox row");

        (await InboxAsync(second)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await LedgerAsync(paid.SubscriptionId)).Count(x => x.Type == LedgerEntryType.Refund).Should().Be(1);
        (await CreditNotesAsync(paid.SubscriptionId)).Should().Be(1);
        (await fx.WithDb(db => db.OnlinePaymentRefunds.CountAsync(x => x.ProviderRefundId == "re_dup_id"))).Should().Be(1);
    }

    [Fact]
    public async Task Refund_beyond_the_money_received_is_rejected_and_books_nothing()
    {
        var paid = await PaidAsync();
        var tooMuch = EventId();
        var overCap = EventId();

        await RefundWebhookAsync(paid.SessionId, paid.Amount + 0.01m, "re_too_much", tooMuch);
        await RefundWebhookAsync(paid.SessionId, paid.Amount - 5m, "re_ok");
        await RefundWebhookAsync(paid.SessionId, 5.01m, "re_over_cap", overCap);

        foreach (var id in new[] { tooMuch, overCap })
        {
            var inbox = await InboxAsync(id);
            inbox.Status.Should().Be(InboundWebhookStatus.Failed);
            inbox.LastError.Should().Contain("exceeds the money received");
        }

        var refunds = (await LedgerAsync(paid.SubscriptionId)).Where(x => x.Type == LedgerEntryType.Refund).ToList();
        refunds.Should().ContainSingle().Which.Amount.Should().Be(-(paid.Amount - 5m));
        (await SessionAsync(paid.SessionId)).Status.Should().Be(OnlinePaymentSessionStatus.Succeeded);
    }

    [Fact]
    public async Task Refund_exactly_up_to_the_cap_is_accepted_and_one_more_cent_is_not()
    {
        var paid = await PaidAsync();
        var extra = EventId();

        await RefundWebhookAsync(paid.SessionId, paid.Amount, "re_all");
        await RefundWebhookAsync(paid.SessionId, 0.01m, "re_extra", extra);

        (await InboxAsync(extra)).Status.Should().Be(InboundWebhookStatus.Failed);
        (await LedgerAsync(paid.SubscriptionId)).Where(x => x.Type == LedgerEntryType.Refund).Sum(x => x.Amount).Should().Be(-paid.Amount);
    }

    [Fact]
    public async Task Refund_for_a_payment_that_was_never_settled_is_rejected()
    {
        var plan = await fx.CreatePlanAsync($"OnlineRefundUnsettled-{Guid.NewGuid():N}", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        var checkout = await fx.SendAs(subscriberId, new CreateOnlineCheckoutCommand(
            created.SubscriptionId, subscriberId, Fake, ReturnUrl, ReturnUrl));
        var id = EventId();

        await RefundWebhookAsync(checkout.SessionId, 10m, "re_unsettled", id);

        var inbox = await InboxAsync(id);
        inbox.Status.Should().Be(InboundWebhookStatus.Failed);
        inbox.LastError.Should().Contain("never settled");
        (await LedgerAsync(created.SubscriptionId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Refund_with_a_zero_amount_unknown_reference_or_wrong_currency_is_rejected()
    {
        var paid = await PaidAsync();
        var zero = EventId();
        var unknown = EventId();
        var currency = EventId();

        await RefundWebhookAsync(paid.SessionId, 0m, "re_zero", zero);
        await RefundWebhookAsync("fake_does_not_exist", 10m, "re_unknown", unknown);
        await RefundWebhookAsync(paid.SessionId, 10m, "re_usd", currency, "USD");

        (await InboxAsync(zero)).LastError.Should().Contain("positive");
        (await InboxAsync(unknown)).LastError.Should().Contain("Unknown payment reference");
        (await InboxAsync(currency)).LastError.Should().Contain("Currency mismatch");
        (await LedgerAsync(paid.SubscriptionId)).Should().NotContain(x => x.Type == LedgerEntryType.Refund);
    }

    [Fact]
    public async Task Provider_refund_reduces_what_a_later_refund_request_can_claim()
    {
        var paid = await PaidAsync(cycle: 12, price: 1150m);
        await RefundWebhookAsync(paid.SessionId, 150m, "re_dashboard");
        await fx.SendAsAdmin(new CancelSubscriptionCommand(paid.SubscriptionId, TestIds.Admin, true, "test", CancellationReason.Other));

        var refundId = await fx.SendAs(paid.SubscriberId, new RequestRefundCommand(paid.SubscriptionId, paid.SubscriberId));

        var request = await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId));
        request.RequestedAmount.Should().Be(paid.Amount - 150m, "money the provider already gave back cannot be refunded again");
    }

    // ----- admin refund of an online payment -----

    private async Task<(Paid Paid, Guid RefundId)> ApprovedRefundAsync()
    {
        var paid = await PaidAsync(cycle: 12, price: 1150m);
        await fx.SendAsAdmin(new CancelSubscriptionCommand(paid.SubscriptionId, TestIds.Admin, true, "test", CancellationReason.Other));
        var refundId = await fx.SendAs(paid.SubscriberId, new RequestRefundCommand(paid.SubscriptionId, paid.SubscriberId));
        var request = await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId));
        await fx.SendAsAdmin(new AdminReviewRefundCommand(refundId, TestIds.Admin, true, "ok", request.Version));
        return (paid, refundId);
    }

    [Fact]
    public async Task Admin_refund_of_an_online_payment_goes_through_the_gateway_and_its_echo_webhook_is_not_booked_twice()
    {
        var (paid, refundId) = await ApprovedRefundAsync();

        await fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "ignored-when-the-provider-refunds"));

        var online = await fx.WithDb(db => db.OnlinePaymentRefunds.AsNoTracking().SingleAsync(x => x.RefundRequestId == refundId));
        online.Amount.Should().Be(paid.Amount);
        online.ProviderRefundId.Should().StartWith("fake_re_");
        online.BookedByWebhook.Should().BeFalse();
        var request = await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId));
        request.Status.Should().Be(RefundStatus.Processed);
        request.ExternalTransactionRef.Should().Be(online.ProviderRefundId, "the provider refund id replaces the admin reference");

        var before = await LedgerAsync(paid.SubscriptionId);
        before.Should().ContainSingle(x => x.Type == LedgerEntryType.Refund && x.RefundId == refundId && x.Amount == -paid.Amount);

        // The provider now echoes its own refund: recognized by id, nothing is booked a second time.
        var echo = EventId();
        await RefundWebhookAsync(paid.SessionId, paid.Amount, online.ProviderRefundId, echo);

        (await InboxAsync(echo)).Status.Should().Be(InboundWebhookStatus.Processed);
        (await LedgerAsync(paid.SubscriptionId)).Should().HaveCount(before.Count);
        (await CreditNotesAsync(paid.SubscriptionId)).Should().Be(1);
    }

    [Fact]
    public async Task Admin_refund_declined_by_the_provider_books_nothing_and_stays_approved()
    {
        var (paid, refundId) = await ApprovedRefundAsync();
        await fx.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE subscriptions.online_payment_sessions SET provider_reference = {"declined_" + paid.SessionId} WHERE provider_reference = {paid.SessionId}"));

        var act = () => fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "REF-1"));

        await act.Should().ThrowAsync<PaymentGatewayException>();
        var request = await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId));
        request.Status.Should().Be(RefundStatus.Approved);
        (await LedgerAsync(paid.SubscriptionId)).Should().NotContain(x => x.Type == LedgerEntryType.Refund);
        (await CreditNotesAsync(paid.SubscriptionId)).Should().Be(0);
        (await fx.WithDb(db => db.OnlinePaymentRefunds.CountAsync(x => x.SubscriptionId == paid.SubscriptionId))).Should().Be(0);
    }

    [Fact]
    public async Task Admin_refund_with_a_stale_version_is_rejected_before_the_provider_is_called()
    {
        var (paid, refundId) = await ApprovedRefundAsync();

        var act = () => fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "REF-1", Version: 999));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await fx.WithDb(db => db.OnlinePaymentRefunds.CountAsync(x => x.SubscriptionId == paid.SubscriptionId))).Should().Be(0);
    }

    [Fact]
    public async Task Admin_refund_of_a_manual_payment_does_not_call_a_provider()
    {
        var plan = await fx.CreatePlanAsync($"ManualRefund-{Guid.NewGuid():N}", 1150m, cycle: 12);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, "test", CancellationReason.Other));
        var refundId = await fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));
        var request = await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId));
        await fx.SendAsAdmin(new AdminReviewRefundCommand(refundId, TestIds.Admin, true, "ok", request.Version));

        await fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "BANK-REF-9"));

        (await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId))).ExternalTransactionRef
            .Should().Be("BANK-REF-9");
        (await fx.WithDb(db => db.OnlinePaymentRefunds.CountAsync(x => x.SubscriptionId == subscriptionId))).Should().Be(0);
        (await LedgerAsync(subscriptionId)).Should().ContainSingle(x => x.Type == LedgerEntryType.Refund && x.Amount == -1150m);
    }

    // ----- interrupted / concurrent admin refunds (no second provider refund) -----

    private Task SetProcessingStartedAsync(Guid refundId, DateTimeOffset startedAt) =>
        fx.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE subscriptions.subscription_refund_requests SET processing_started_at = {startedAt} WHERE id = {refundId}"));

    private Task<int> ProviderRefundCountAsync(Guid subscriptionId) =>
        fx.WithDb(db => db.OnlinePaymentRefunds.CountAsync(x => x.SubscriptionId == subscriptionId));

    [Fact]
    public async Task Retry_after_a_lost_response_adopts_the_webhook_booked_refund_instead_of_refunding_again()
    {
        var (paid, refundId) = await ApprovedRefundAsync();
        var requested = Math.Round(paid.Amount / 2m, 2);
        await fx.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE subscriptions.subscription_refund_requests SET requested_amount = {requested} WHERE id = {refundId}"));

        // The first attempt reached the provider (refund made), then died before saving: only the marker survived and
        // the provider's webhook booked the money unlinked.
        await SetProcessingStartedAsync(refundId, DateTimeOffset.UtcNow.AddMinutes(-10));
        await RefundWebhookAsync(paid.SessionId, requested, "re_lost_response");

        await fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "REF-RETRY"));

        (await ProviderRefundCountAsync(paid.SubscriptionId)).Should().Be(1, "the retry must not refund at the provider again");
        var online = await fx.WithDb(db => db.OnlinePaymentRefunds.AsNoTracking().SingleAsync(x => x.SubscriptionId == paid.SubscriptionId));
        online.ProviderRefundId.Should().Be("re_lost_response");
        online.RefundRequestId.Should().Be(refundId);
        (await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId))).Status
            .Should().Be(RefundStatus.Processed);
        (await LedgerAsync(paid.SubscriptionId)).Where(x => x.Type == LedgerEntryType.Refund).Sum(x => x.Amount)
            .Should().Be(-requested, "the money is booked once, by the webhook");
    }

    [Fact]
    public async Task Admin_refund_while_another_attempt_is_in_flight_is_rejected_with_409_and_the_provider_is_not_called()
    {
        var (paid, refundId) = await ApprovedRefundAsync();
        await SetProcessingStartedAsync(refundId, DateTimeOffset.UtcNow);

        var act = () => fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "REF-2"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*already being processed*");
        (await ProviderRefundCountAsync(paid.SubscriptionId)).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_double_submit_refunds_at_the_provider_exactly_once()
    {
        var (paid, refundId) = await ApprovedRefundAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "REF-DOUBLE"));
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }));

        results.Count(x => x is null).Should().Be(1);
        results.Single(x => x is not null).Should().Match(x => x is ConflictException || x is ConcurrencyConflictException);
        (await ProviderRefundCountAsync(paid.SubscriptionId)).Should().Be(1);
        (await LedgerAsync(paid.SubscriptionId)).Count(x => x.Type == LedgerEntryType.Refund).Should().Be(1);
    }

    [Fact]
    public async Task Retry_on_a_provider_without_idempotency_does_not_call_it_again_and_waits_for_the_webhook()
    {
        var (paid, refundId) = await ApprovedRefundAsync();
        await fx.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE subscriptions.online_payment_sessions SET provider = {"Paymob"}, provider_payment_reference = {"123"} WHERE subscription_id = {paid.SubscriptionId}"));
        await SetProcessingStartedAsync(refundId, DateTimeOffset.UtcNow.AddMinutes(-10));

        var act = () => fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "REF-3"));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*pending at the provider*");
        (await ProviderRefundCountAsync(paid.SubscriptionId)).Should().Be(0);
        (await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId))).Status
            .Should().Be(RefundStatus.Approved);
    }
}
