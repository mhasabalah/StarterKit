using Webhooks.Domains.Endpoints.Events;
using Webhooks.Domains.Endpoints.Exceptions;
using Webhooks.Domains.Endpoints.Models;

namespace Webhooks.Tests.Domain;

public class WebhookEndpointTests
{
    [Fact]
    public void Create_StartsEnabled_WithoutFailures()
    {
        var endpoint = TestSupport.NewEndpoint();

        endpoint.Enabled.Should().BeTrue();
        endpoint.ConsecutiveFailures.Should().Be(0);
        endpoint.Id.Should().NotBeEmpty();
        endpoint.SecretProtected.Should().Be("protected-secret");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutUrl_IsRejected(string url)
    {
        var act = () => WebhookEndpoint.Create(url, null, ["a"], "s");

        act.Should().Throw<InvalidWebhookEndpointException>();
    }

    [Fact]
    public void Create_WithoutEventTypes_IsRejected()
    {
        var act = () => WebhookEndpoint.Create("https://x.example.com", null, [" ", ""], "s");

        act.Should().Throw<InvalidWebhookEndpointException>();
    }

    [Fact]
    public void Create_WithoutSecret_IsRejected()
    {
        var act = () => WebhookEndpoint.Create("https://x.example.com", null, ["a"], " ");

        act.Should().Throw<InvalidWebhookEndpointException>();
    }

    [Fact]
    public void Create_TrimsAndDeduplicatesEventTypes()
    {
        var endpoint = WebhookEndpoint.Create(" https://x.example.com ", "  ", [" a ", "a", "b"], "s");

        endpoint.EventTypes.Should().Equal("a", "b");
        endpoint.Url.Should().Be("https://x.example.com");
        endpoint.Description.Should().BeNull();
    }

    [Fact]
    public void Create_WithTooManyEventTypes_IsRejected()
    {
        var types = Enumerable.Range(0, WebhookEndpoint.MaxEventTypes + 1).Select(i => $"type-{i}");

        var act = () => WebhookEndpoint.Create("https://x.example.com", null, types, "s");

        act.Should().Throw<InvalidWebhookEndpointException>();
    }

    [Theory]
    [InlineData("catalog.product-published.v1", true)]
    [InlineData("subscriptions.subscription-expired.v1", false)]
    public void IsSubscribedTo_MatchesExactTypes(string type, bool expected) =>
        TestSupport.NewEndpoint(events: ["catalog.product-published.v1"]).IsSubscribedTo(type).Should().Be(expected);

    [Fact]
    public void IsSubscribedTo_Wildcard_MatchesEverything() =>
        TestSupport.NewEndpoint(events: [WebhookEndpoint.AllEvents]).IsSubscribedTo("anything.v1").Should().BeTrue();

    [Fact]
    public void RecordFailure_BelowThreshold_KeepsEndpointEnabled_AndRaisesNothing()
    {
        var endpoint = TestSupport.NewEndpoint();

        var disabled = endpoint.RecordFailure(TestSupport.Now, disableAfter: 3, "boom");
        endpoint.RecordFailure(TestSupport.Now, 3, "boom");

        disabled.Should().BeFalse();
        endpoint.Enabled.Should().BeTrue();
        endpoint.ConsecutiveFailures.Should().Be(2);
        endpoint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RecordFailure_AtThreshold_DisablesOnce_AndRaisesOneEvent()
    {
        var endpoint = TestSupport.NewEndpoint();
        endpoint.RecordFailure(TestSupport.Now, 3, "e1");
        endpoint.RecordFailure(TestSupport.Now, 3, "e2");

        var disabled = endpoint.RecordFailure(TestSupport.Now, 3, "HTTP 500");
        var again = endpoint.RecordFailure(TestSupport.Now, 3, "HTTP 500");

        disabled.Should().BeTrue();
        again.Should().BeFalse("an already-disabled endpoint must not notify again");
        endpoint.Enabled.Should().BeFalse();
        endpoint.DisabledAt.Should().Be(TestSupport.Now);
        endpoint.DisabledReason.Should().Contain("3 consecutive failures").And.Contain("HTTP 500");
        endpoint.DomainEvents.OfType<WebhookEndpointAutoDisabledEvent>().Should().ContainSingle()
            .Which.Should().Match<WebhookEndpointAutoDisabledEvent>(e => e.EndpointId == endpoint.Id && e.ConsecutiveFailures == 3);
    }

    [Fact]
    public void RecordSuccess_ResetsTheFailureStreak()
    {
        var endpoint = TestSupport.NewEndpoint();
        endpoint.RecordFailure(TestSupport.Now, 5, "x");
        endpoint.RecordFailure(TestSupport.Now, 5, "x");

        endpoint.RecordSuccess(TestSupport.Now);

        endpoint.ConsecutiveFailures.Should().Be(0);
        endpoint.LastSuccessAt.Should().Be(TestSupport.Now);
    }

    [Fact]
    public void Update_ReEnabling_ClearsTheDisabledState()
    {
        var endpoint = TestSupport.NewEndpoint();
        for (var i = 0; i < 3; i++)
            endpoint.RecordFailure(TestSupport.Now, 3, "x");
        endpoint.Enabled.Should().BeFalse();

        endpoint.Update("https://new.example.com", "d", ["b"], enabled: true, TestSupport.Now);

        endpoint.Enabled.Should().BeTrue();
        endpoint.ConsecutiveFailures.Should().Be(0);
        endpoint.DisabledAt.Should().BeNull();
        endpoint.DisabledReason.Should().BeNull();
        endpoint.Url.Should().Be("https://new.example.com");
        endpoint.EventTypes.Should().Equal("b");
    }

    [Fact]
    public void Update_KeepingItEnabled_DoesNotResetTheFailureCounter()
    {
        var endpoint = TestSupport.NewEndpoint();
        endpoint.RecordFailure(TestSupport.Now, 10, "x");

        endpoint.Update("https://x.example.com", null, ["a"], enabled: true, TestSupport.Now);

        endpoint.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void Update_Disabling_StoresAReason_AndRaisesNoNotification()
    {
        var endpoint = TestSupport.NewEndpoint();

        endpoint.Update("https://x.example.com", null, ["a"], enabled: false, TestSupport.Now);

        endpoint.Enabled.Should().BeFalse();
        endpoint.DisabledAt.Should().Be(TestSupport.Now);
        endpoint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RotateSecret_ReplacesTheStoredSecret()
    {
        var endpoint = TestSupport.NewEndpoint();

        endpoint.RotateSecret("new-protected");

        endpoint.SecretProtected.Should().Be("new-protected");
    }
}
