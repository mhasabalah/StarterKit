using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Webhooks.Contracts.Deliveries;
using Webhooks.Contracts.Endpoints;
using Webhooks.Domains.Deliveries.Jobs;
using Webhooks.Security;
using static Webhooks.IntegrationTests.WebhooksPostgresFixture;

namespace Webhooks.IntegrationTests;

[Collection(WebhooksPostgresCollection.Name)]
public class ForwardingAndDeliveryTests(WebhooksPostgresFixture fx)
{
    // ----- fan-out --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_public_event_creates_one_delivery_per_subscribed_enabled_endpoint_of_that_tenant_only()
    {
        var (subscribed, _) = await Arrange.Endpoint(fx, TenantA, null, Arrange.ProductPublished);
        var (wildcard, _) = await Arrange.Endpoint(fx, TenantA, null, "*");
        var (other, _) = await Arrange.Endpoint(fx, TenantA, null, Arrange.SubscriptionExpired);
        var (disabled, _) = await Arrange.Endpoint(fx, TenantA, null, Arrange.ProductPublished);
        await fx.AdminExecAsync($"update webhooks.webhook_endpoints set enabled=false where id='{disabled}'");
        var (tenantBEndpoint, _) = await Arrange.Endpoint(fx, TenantB, null, "*");
        var evt = Arrange.NewProductEvent();

        await Arrange.Consume(fx, TenantA, evt);

        var created = (await Arrange.Deliveries(fx, TenantA)).Where(d => d.EventId == evt.EventId).ToList();
        var targets = created.Select(d => d.EndpointId).ToList();
        targets.Should().Contain([subscribed, wildcard]).And.NotContain([other, disabled]);
        created.Should().OnlyContain(d => d.Status == WebhookDeliveryStatus.Pending && d.EventType == Arrange.ProductPublished);
        (await Arrange.Deliveries(fx, TenantB, tenantBEndpoint)).Should().NotContain(d => d.EventId == evt.EventId);
        foreach (var d in created)
            fx.Jobs.ForDelivery(d.Id).Should().ContainSingle();

        using var payload = JsonDocument.Parse(created[0].Payload);
        payload.RootElement.GetProperty("type").GetString().Should().Be(Arrange.ProductPublished);
        payload.RootElement.GetProperty("tenantId").GetGuid().Should().Be(TenantA.Id);
        payload.RootElement.GetProperty("data").GetProperty("sku").GetString().Should().Be(evt.Sku);
    }

    [Fact]
    public async Task The_same_event_delivered_twice_by_the_bus_creates_no_second_delivery_and_no_second_job()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA, null, Arrange.ProductPublished);
        var evt = Arrange.NewProductEvent();

        await Arrange.Consume(fx, TenantA, evt);
        await Arrange.Consume(fx, TenantA, evt);

        var deliveries = (await Arrange.Deliveries(fx, TenantA, endpoint)).Where(d => d.EventId == evt.EventId).ToList();
        deliveries.Should().ContainSingle();
        fx.Jobs.ForDelivery(deliveries[0].Id).Should().ContainSingle();
    }

    [Fact]
    public async Task A_subscription_event_without_a_tenant_header_is_forwarded_to_the_subscriber_tenant()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA, null, Arrange.SubscriptionExpired);
        var (bEndpoint, _) = await Arrange.Endpoint(fx, TenantB, null, Arrange.SubscriptionExpired);
        var evt = new SubscriptionExpiredIntegrationEvent { SubscriberId = TenantA.Id, SubscriptionId = Guid.NewGuid() };

        await Arrange.Consume(fx, tenant: null, evt);

        (await Arrange.Deliveries(fx, TenantA, endpoint)).Should().ContainSingle(d => d.EventId == evt.EventId);
        (await Arrange.Deliveries(fx, TenantB, bEndpoint)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_subscription_event_for_a_subscriber_that_is_not_a_tenant_is_ignored()
    {
        var before = fx.Jobs.All.Count;

        await Arrange.Consume(fx, null, new SubscriptionExpiredIntegrationEvent { SubscriberId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid() });

        fx.Jobs.All.Count.Should().Be(before);
    }

    // ----- delivery job: real HTTP, signature -------------------------------------------------------------------------

    [Fact]
    public async Task A_delivery_is_posted_with_standard_webhooks_headers_and_a_signature_the_receiver_can_verify()
    {
        await using var receiver = new TestReceiver();
        fx.Sender.Current = new WebhookHttpSender(Microsoft.Extensions.Options.Options.Create(
            new Webhooks.Options.WebhooksOptions { AllowPrivateNetworks = true, RequestTimeoutSeconds = 5 }));
        var (endpoint, secret) = await Arrange.Endpoint(fx, TenantA, receiver.Url);
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);

        var outcome = await fx.RunDeliveryAsync(TenantA, delivery.Id);

        outcome.Should().Be(WebhookAttemptOutcome.Succeeded);
        var request = receiver.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("POST");
        request.Headers["content-type"].Should().StartWith("application/json");
        request.Headers["webhook-id"].Should().Be(WebhookSigner.MessageIdHeader(delivery.EventId));
        request.Body.Should().Be(delivery.Payload);
        WebhookSigner.Verify(secret, request.Headers["webhook-id"], request.Headers["webhook-timestamp"], request.Body,
            request.Headers["webhook-signature"], fx.Clock.GetUtcNow()).Should().BeTrue();
        WebhookSigner.Verify(WebhookSigner.GenerateSecret(), request.Headers["webhook-id"], request.Headers["webhook-timestamp"],
            request.Body, request.Headers["webhook-signature"], fx.Clock.GetUtcNow()).Should().BeFalse();

        var stored = await Arrange.Delivery(fx, TenantA, delivery.Id);
        stored.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        stored.AttemptCount.Should().Be(1);
        stored.LastResponseCode.Should().Be(200);
        stored.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_rotated_secret_signs_later_deliveries_and_the_old_one_stops_verifying()
    {
        await using var receiver = new TestReceiver();
        fx.Sender.Current = new WebhookHttpSender(Microsoft.Extensions.Options.Options.Create(
            new Webhooks.Options.WebhooksOptions { AllowPrivateNetworks = true, RequestTimeoutSeconds = 5 }));
        var (endpoint, oldSecret) = await Arrange.Endpoint(fx, TenantA, receiver.Url);
        var rotated = await fx.SendAsync(TenantA, new RotateWebhookEndpointSecretCommand(endpoint));
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);

        await fx.RunDeliveryAsync(TenantA, delivery.Id);

        var request = receiver.Requests.Single();
        var verify = (string s) => WebhookSigner.Verify(s, request.Headers["webhook-id"], request.Headers["webhook-timestamp"],
            request.Body, request.Headers["webhook-signature"], fx.Clock.GetUtcNow());
        verify(rotated.Secret).Should().BeTrue();
        verify(oldSecret).Should().BeFalse();
    }

    // ----- delivery job: retries ------------------------------------------------------------------------------------

    [Fact]
    public async Task Transient_failures_are_retried_with_growing_delays_until_one_succeeds()
    {
        var sender = fx.Sender.Scripted().Then(ScriptedSender.Http(500)).Then(ScriptedSender.Timeout());
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);
        var enqueuedBefore = fx.Jobs.ForDelivery(delivery.Id).Count;

        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.WillRetry);
        var afterFirst = await Arrange.Delivery(fx, TenantA, delivery.Id);
        afterFirst.Status.Should().Be(WebhookDeliveryStatus.Pending);
        afterFirst.AttemptCount.Should().Be(1);
        afterFirst.NextAttemptAt.Should().BeCloseTo(fx.Clock.GetUtcNow().UtcDateTime.AddSeconds(10), TimeSpan.FromMilliseconds(5));
        var scheduled = fx.Jobs.ForDelivery(delivery.Id).Skip(enqueuedBefore).Should().ContainSingle().Subject;
        scheduled.Delay.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));

        // Too early: the retry is not due, nothing is sent.
        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.Skipped);
        sender.Requests.Should().HaveCount(1);

        fx.Clock.Advance(TimeSpan.FromSeconds(11));
        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.WillRetry);
        fx.Jobs.ForDelivery(delivery.Id).Last().Delay.Should().BeCloseTo(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(50));

        fx.Clock.Advance(TimeSpan.FromSeconds(61));
        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.Succeeded);

        var done = await Arrange.Delivery(fx, TenantA, delivery.Id);
        done.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        done.AttemptCount.Should().Be(3);
        (await Arrange.EndpointRow(fx, TenantA, endpoint)).ConsecutiveFailures.Should().Be(0);
        sender.Requests.Select(r => r.MessageId).Distinct().Should().ContainSingle("the webhook-id is stable across attempts");
    }

    [Fact]
    public async Task A_delivery_that_keeps_failing_stops_after_MaxAttempts_and_is_failed()
    {
        fx.Sender.Scripted().Fallback = _ => Task.FromResult(ScriptedSender.Http(503));
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);
        var max = fx.Services.GetRequiredService<IOptions<Webhooks.Options.WebhooksOptions>>().Value.MaxAttempts;

        WebhookAttemptOutcome last = default;
        for (var i = 0; i < max; i++)
        {
            last = await fx.RunDeliveryAsync(TenantA, delivery.Id);
            fx.Clock.Advance(TimeSpan.FromHours(7));
        }

        last.Should().Be(WebhookAttemptOutcome.Failed);
        var stored = await Arrange.Delivery(fx, TenantA, delivery.Id);
        stored.Status.Should().Be(WebhookDeliveryStatus.Failed);
        stored.AttemptCount.Should().Be(max);
        stored.NextAttemptAt.Should().BeNull();
        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.Skipped);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(302)]
    public async Task A_permanent_failure_is_not_retried(int status)
    {
        fx.Sender.Scripted().Then(ScriptedSender.Http(status));
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);
        var jobsBefore = fx.Jobs.ForDelivery(delivery.Id).Count;

        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.Failed);

        (await Arrange.Delivery(fx, TenantA, delivery.Id)).Status.Should().Be(WebhookDeliveryStatus.Failed);
        fx.Jobs.ForDelivery(delivery.Id).Count.Should().Be(jobsBefore, "no retry scheduled");
    }

    [Fact]
    public async Task Two_workers_racing_for_the_same_delivery_send_it_only_once()
    {
        var sender = fx.Sender.Scripted();
        sender.Fallback = async _ =>
        {
            await Task.Delay(300);
            return ScriptedSender.Ok();
        };
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);

        var outcomes = await Task.WhenAll(fx.RunDeliveryAsync(TenantA, delivery.Id), fx.RunDeliveryAsync(TenantA, delivery.Id));

        sender.Requests.Should().HaveCount(1);
        outcomes.Should().Contain(WebhookAttemptOutcome.Succeeded).And.Contain(WebhookAttemptOutcome.Skipped);
    }

    [Fact]
    public async Task Repeated_failures_disable_the_endpoint_and_tell_the_owner_once()
    {
        fx.Sender.Scripted().Fallback = _ => Task.FromResult(ScriptedSender.Http(500));
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantB);
        var threshold = fx.Services.GetRequiredService<IOptions<Webhooks.Options.WebhooksOptions>>().Value.AutoDisableAfterConsecutiveFailures;
        var mailsBefore = fx.Notifications.Sent.Count(m => m.User == OwnerOfB);

        var deliveries = new List<Guid>();
        for (var i = 0; i < threshold + 2; i++)
            deliveries.Add((await Arrange.OneDelivery(fx, TenantB, endpoint)).Id);
        foreach (var id in deliveries)
            await fx.RunDeliveryAsync(TenantB, id);

        var row = await Arrange.EndpointRow(fx, TenantB, endpoint);
        row.Enabled.Should().BeFalse();
        row.DisabledAt.Should().NotBeNull();
        row.DisabledReason.Should().Contain("consecutive failures");
        var mails = fx.Notifications.Sent.Where(m => m.User == OwnerOfB).Skip(mailsBefore).ToList();
        mails.Should().ContainSingle();
        mails[0].Template.Should().Be(Notification.Contracts.Enums.EmailTemplate.WebhookEndpointDisabled);
        mails[0].Model!["EndpointAddress"].Should().Be(row.Url);

        // Later deliveries to the disabled endpoint are cancelled without being sent.
        var (second, _) = await Arrange.Endpoint(fx, TenantB);
        var late = await Arrange.OneDelivery(fx, TenantB, second);
        await fx.AdminExecAsync($"update webhooks.webhook_endpoints set enabled=false where id='{second}'");
        var sent = fx.Sender.Current.As<ScriptedSender>().Requests.Count;

        (await fx.RunDeliveryAsync(TenantB, late.Id)).Should().Be(WebhookAttemptOutcome.Cancelled);

        (await Arrange.Delivery(fx, TenantB, late.Id)).Status.Should().Be(WebhookDeliveryStatus.Cancelled);
        fx.Sender.Current.As<ScriptedSender>().Requests.Count.Should().Be(sent, "nothing is sent to a disabled endpoint");
    }

    [Fact]
    public async Task A_delivery_job_cannot_touch_another_tenants_delivery()
    {
        var sender = fx.Sender.Scripted();
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);

        (await fx.RunDeliveryAsync(TenantB, delivery.Id)).Should().Be(WebhookAttemptOutcome.Skipped);

        sender.Requests.Should().BeEmpty();
        (await Arrange.Delivery(fx, TenantA, delivery.Id)).AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task A_secret_that_cannot_be_decrypted_fails_the_delivery_permanently_without_sending()
    {
        var sender = fx.Sender.Scripted();
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        await fx.AdminExecAsync($"update webhooks.webhook_endpoints set secret_protected='garbage' where id='{endpoint}'");
        var delivery = await Arrange.OneDelivery(fx, TenantA, endpoint);

        (await fx.RunDeliveryAsync(TenantA, delivery.Id)).Should().Be(WebhookAttemptOutcome.Failed);

        sender.Requests.Should().BeEmpty();
        (await Arrange.Delivery(fx, TenantA, delivery.Id)).LastError.Should().Contain("rotate");
    }

    // ----- ping / redeliver / log -----------------------------------------------------------------------------------

    [Fact]
    public async Task Ping_queues_a_signed_ping_delivery()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);

        var result = await fx.SendAsync(TenantA, new SendWebhookTestPingCommand(endpoint));

        var stored = await Arrange.Delivery(fx, TenantA, result.DeliveryId);
        stored.EventType.Should().Be("webhook.ping");
        fx.Jobs.ForDelivery(result.DeliveryId).Should().ContainSingle();
    }

    [Fact]
    public async Task Ping_to_a_disabled_endpoint_is_rejected()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        await fx.AdminExecAsync($"update webhooks.webhook_endpoints set enabled=false where id='{endpoint}'");

        var act = () => fx.SendAsync(TenantA, new SendWebhookTestPingCommand(endpoint));

        await act.Should().ThrowAsync<Shared.Core.Exceptions.BadRequestException>();
    }

    [Fact]
    public async Task Redeliver_repeats_a_finished_delivery_with_the_same_webhook_id()
    {
        fx.Sender.Scripted().Then(ScriptedSender.Http(400));
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var original = await Arrange.OneDelivery(fx, TenantA, endpoint);
        await fx.RunDeliveryAsync(TenantA, original.Id);

        var result = await fx.SendAsync(TenantA, new RedeliverWebhookCommand(original.Id));

        var repeat = await Arrange.Delivery(fx, TenantA, result.DeliveryId);
        repeat.EventId.Should().Be(original.EventId);
        repeat.RedeliveryOfId.Should().Be(original.Id);
        repeat.Status.Should().Be(WebhookDeliveryStatus.Pending);
        fx.Jobs.ForDelivery(repeat.Id).Should().ContainSingle();
        (await fx.RunDeliveryAsync(TenantA, repeat.Id)).Should().Be(WebhookAttemptOutcome.Succeeded);
    }

    [Fact]
    public async Task Redeliver_of_a_pending_delivery_is_a_conflict()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var pending = await Arrange.OneDelivery(fx, TenantA, endpoint);

        var act = () => fx.SendAsync(TenantA, new RedeliverWebhookCommand(pending.Id));

        await act.Should().ThrowAsync<Shared.Core.Exceptions.ConflictException>();
    }

    [Fact]
    public async Task The_delivery_log_filters_by_endpoint_and_status_and_shows_the_payload_in_detail()
    {
        fx.Sender.Scripted().Then(ScriptedSender.Http(400));
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var failed = await Arrange.OneDelivery(fx, TenantA, endpoint);
        await fx.RunDeliveryAsync(TenantA, failed.Id);
        await Arrange.OneDelivery(fx, TenantA, endpoint);

        var onlyFailed = await fx.SendAsync(TenantA, new GetWebhookDeliveriesQuery { EndpointId = endpoint, Status = WebhookDeliveryStatus.Failed });
        var all = await fx.SendAsync(TenantA, new GetWebhookDeliveriesQuery { EndpointId = endpoint, PageSize = 100 });
        var detail = await fx.SendAsync(TenantA, new GetWebhookDeliveryByIdQuery(failed.Id));

        onlyFailed.Result!.Should().ContainSingle().Which.Id.Should().Be(failed.Id);
        all.Result!.Should().HaveCount(2);
        detail.Payload.Should().Be(failed.Payload);
        detail.LastResponseCode.Should().Be(400);
    }
}
