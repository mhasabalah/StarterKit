using Shared.Core.Net;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Webhooks.Domains.Deliveries.Jobs;
using Webhooks.Options;
using Webhooks.Security;
using static Webhooks.IntegrationTests.WebhooksPostgresFixture;

namespace Webhooks.IntegrationTests;

/// <summary>The REAL guarded HTTP sender against a real socket, plus SSRF rejection end to end.</summary>
public class SenderTests
{
    private static WebhookHttpSender Sender(bool allowPrivate, int timeoutSeconds = 5, int maxResponseBytes = 64 * 1024,
        GuardedConnector? connector = null) =>
        new(Microsoft.Extensions.Options.Options.Create(new WebhooksOptions
        {
            AllowPrivateNetworks = allowPrivate,
            RequestTimeoutSeconds = timeoutSeconds,
            MaxResponseBytes = maxResponseBytes
        }), connector);

    private static WebhookRequest Request(string url) => new(url, Guid.NewGuid(), 1, "v1,sig", "{}");

    [Fact]
    public async Task A_2xx_answer_is_a_success()
    {
        await using var receiver = new TestReceiver();

        var result = await Sender(true).SendAsync(Request(receiver.Url), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        receiver.Requests.Single().Headers["user-agent"].Should().StartWith("StarterKit-Webhooks");
    }

    [Theory]
    [InlineData(500, WebhookFailureKind.Retriable)]
    [InlineData(503, WebhookFailureKind.Retriable)]
    [InlineData(429, WebhookFailureKind.Retriable)]
    [InlineData(408, WebhookFailureKind.Retriable)]
    [InlineData(400, WebhookFailureKind.Permanent)]
    [InlineData(401, WebhookFailureKind.Permanent)]
    [InlineData(404, WebhookFailureKind.Permanent)]
    [InlineData(410, WebhookFailureKind.Permanent)]
    public async Task Failure_status_codes_are_classified(int status, WebhookFailureKind expected)
    {
        await using var receiver = new TestReceiver { Respond = _ => new ReceiverResponse(status, "denied") };

        var result = await Sender(true).SendAsync(Request(receiver.Url), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(status);
        result.Failure.Should().Be(expected);
        result.ResponseSnippet.Should().Be("denied");
    }

    [Fact]
    public async Task Redirects_are_never_followed()
    {
        await using var target = new TestReceiver();
        await using var redirector = new TestReceiver
        {
            Respond = _ => new ReceiverResponse(302, "", Headers: new Dictionary<string, string> { ["Location"] = target.Url })
        };

        var result = await Sender(true).SendAsync(Request(redirector.Url), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(302);
        result.Failure.Should().Be(WebhookFailureKind.Permanent);
        target.Requests.Should().BeEmpty("a redirect could point at an internal address");
    }

    [Fact]
    public async Task A_2xx_whose_body_stalls_past_the_deadline_is_still_a_success_with_the_partial_snippet()
    {
        await using var receiver = new TestReceiver
        {
            Respond = _ => new ReceiverResponse(200, "accepted-and-processed", StallAfterPartialBody: TimeSpan.FromSeconds(10))
        };

        var result = await Sender(true, timeoutSeconds: 1).SendAsync(Request(receiver.Url), CancellationToken.None);

        result.Success.Should().BeTrue("the receiver already answered 2xx; reporting a timeout would deliver the event twice");
        result.StatusCode.Should().Be(200);
        result.Failure.Should().Be(WebhookFailureKind.None);
        result.ResponseSnippet.Should().Be("accepted-an");
    }

    [Fact]
    public async Task A_slow_endpoint_times_out_as_a_transient_failure()
    {
        await using var receiver = new TestReceiver { Respond = _ => new ReceiverResponse(Delay: TimeSpan.FromSeconds(10)) };

        var result = await Sender(true, timeoutSeconds: 1).SendAsync(Request(receiver.Url), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure.Should().Be(WebhookFailureKind.Retriable);
        result.Error.Should().Contain("timed out");
        result.DurationMs.Should().BeLessThan(5000);
    }

    [Fact]
    public async Task A_huge_response_is_capped_and_never_buffered_whole()
    {
        await using var receiver = new TestReceiver { Respond = _ => new ReceiverResponse(200, new string('x', 2_000_000)) };

        var result = await Sender(true, maxResponseBytes: 1024).SendAsync(Request(receiver.Url), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ResponseSnippet!.Length.Should().BeLessThanOrEqualTo(1024);
    }

    [Fact]
    public async Task Connection_refused_is_a_transient_failure()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await Sender(true).SendAsync(Request($"http://127.0.0.1:{port}/x"), CancellationToken.None);

        result.Failure.Should().Be(WebhookFailureKind.Retriable);
    }

    // ----- SSRF, end to end through the real handler --------------------------------------------------------------

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("fd00::1")]
    public async Task A_name_resolving_to_a_private_address_is_blocked_before_any_connection(string resolvedTo)
    {
        var connector = new GuardedConnector(false, (_, _) => Task.FromResult(new[] { IPAddress.Parse(resolvedTo) }));

        var result = await Sender(false, connector: connector)
            .SendAsync(Request("http://innocent.example.com/hook"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Failure.Should().Be(WebhookFailureKind.Permanent);
        result.Error.Should().StartWith("Blocked");
        result.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task A_loopback_receiver_is_blocked_when_private_networks_are_not_allowed()
    {
        await using var receiver = new TestReceiver();

        var result = await Sender(false).SendAsync(Request(receiver.Url), CancellationToken.None);

        result.Failure.Should().Be(WebhookFailureKind.Permanent);
        result.Error.Should().StartWith("Blocked");
        receiver.Requests.Should().BeEmpty("the socket must never have been opened");
    }

    [Fact]
    public async Task The_real_resolver_blocks_localhost_through_the_sender()
    {
        var result = await Sender(false).SendAsync(Request("http://localhost:9/hook"), CancellationToken.None);

        result.Failure.Should().Be(WebhookFailureKind.Permanent);
        result.Error.Should().StartWith("Blocked");
    }

    [Fact]
    public async Task A_dns_answer_that_changes_between_calls_is_checked_every_time()
    {
        var answers = new Queue<IPAddress>([IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.5")]);
        var connector = new GuardedConnector(false, (_, _) => Task.FromResult(new[] { answers.Dequeue() }));
        // Generous timeout: under a loaded parallel test run a 1 s timeout could cancel the first send before the
        // resolver ran, so the second send would get the public answer and the assertion would be meaningless.
        var sender = Sender(false, timeoutSeconds: 5, connector: connector);

        var first = await sender.SendAsync(Request("http://rebind.example.com/"), CancellationToken.None);
        // A second sender (own connection pool) sharing the connector: the first send's still-pending connect attempt to
        // the public address must not be reused, or the second call would never reach the resolver.
        using var secondSender = Sender(false, timeoutSeconds: 5, connector: connector);
        var second = await secondSender.SendAsync(Request("http://rebind.example.com/"), CancellationToken.None);

        first.Error.Should().NotStartWith("Blocked", "the first answer was public (the connection itself then fails or times out)");
        second.Error.Should().StartWith("Blocked", "rebinding to a private address is caught on the next connect");
    }
}

[Collection(WebhooksPostgresCollection.Name)]
public class MaintenanceJobTests(WebhooksPostgresFixture fx)
{
    [Fact]
    public async Task The_sweep_re_enqueues_a_lost_delivery_once_and_ignores_fresh_ones()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var lost = await Arrange.OneDelivery(fx, TenantA, endpoint);
        fx.Clock.Advance(TimeSpan.FromMinutes(10));
        var fresh = await Arrange.OneDelivery(fx, TenantA, endpoint);
        var before = fx.Jobs.ForDelivery(lost.Id).Count;

        var first = await fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<DispatchDueWebhooksJob>().DispatchCurrentTenantAsync(null));
        var second = await fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<DispatchDueWebhooksJob>().DispatchCurrentTenantAsync(null));

        first.Should().BeGreaterThanOrEqualTo(1);
        second.Should().Be(0, "the first run pushed NextAttemptAt forward");
        fx.Jobs.ForDelivery(lost.Id).Count.Should().Be(before + 1);
        fx.Jobs.ForDelivery(fresh.Id).Should().ContainSingle("only the original enqueue");
    }

    [Fact]
    public async Task The_sweep_does_not_touch_another_tenants_deliveries()
    {
        var (endpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var lost = await Arrange.OneDelivery(fx, TenantA, endpoint);
        fx.Clock.Advance(TimeSpan.FromMinutes(10));
        var before = fx.Jobs.ForDelivery(lost.Id).Count;

        await fx.InTenantAsync(TenantB, sp => sp.GetRequiredService<DispatchDueWebhooksJob>().DispatchCurrentTenantAsync(null));

        fx.Jobs.ForDelivery(lost.Id).Count.Should().Be(before);
    }

    [Fact]
    public async Task The_purge_removes_old_finished_deliveries_only_for_the_current_tenant()
    {
        var (aEndpoint, _) = await Arrange.Endpoint(fx, TenantA);
        var (bEndpoint, _) = await Arrange.Endpoint(fx, TenantB);
        var aOld = await Arrange.OneDelivery(fx, TenantA, aEndpoint);
        var aOldPending = await Arrange.OneDelivery(fx, TenantA, aEndpoint);
        var aRecent = await Arrange.OneDelivery(fx, TenantA, aEndpoint);
        var bOld = await Arrange.OneDelivery(fx, TenantB, bEndpoint);
        await fx.AdminExecAsync(
            $"update webhooks.webhook_deliveries set status='Succeeded', created_at = now() - interval '90 days' where id in ('{aOld.Id}','{bOld.Id}')");
        await fx.AdminExecAsync(
            $"update webhooks.webhook_deliveries set created_at = now() - interval '90 days' where id = '{aOldPending.Id}'");
        await fx.AdminExecAsync(
            $"update webhooks.webhook_deliveries set status='Succeeded' where id = '{aRecent.Id}'");
        fx.Clock.Set(DateTimeOffset.UtcNow);

        var purged = await fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<PurgeWebhookDeliveriesJob>().PurgeCurrentTenantAsync(null));

        purged.Should().BeGreaterThanOrEqualTo(1);
        (await fx.AdminScalarAsync<long>($"select count(*) from webhooks.webhook_deliveries where id='{aOld.Id}'")).Should().Be(0);
        (await fx.AdminScalarAsync<long>($"select count(*) from webhooks.webhook_deliveries where id='{aOldPending.Id}'")).Should().Be(1, "pending deliveries are never purged");
        (await fx.AdminScalarAsync<long>($"select count(*) from webhooks.webhook_deliveries where id='{aRecent.Id}'")).Should().Be(1);
        (await fx.AdminScalarAsync<long>($"select count(*) from webhooks.webhook_deliveries where id='{bOld.Id}'")).Should().Be(1, "tenant B was not in scope");
    }
}
