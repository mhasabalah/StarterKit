using Webhooks.Domains.Deliveries.Models;

namespace Webhooks.Tests.Domain;

public class WebhookDeliveryTests
{
    private static WebhookDelivery New() =>
        WebhookDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), "catalog.product-published.v1", "{}", TestSupport.Now);

    [Fact]
    public void Create_IsPendingAndDueImmediately()
    {
        var delivery = New();

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
        delivery.IsDue(TestSupport.Now).Should().BeTrue();
    }

    [Fact]
    public void BeginAttempt_LeasesTheDelivery_SoNobodyElseIsDue()
    {
        var delivery = New();

        delivery.BeginAttempt(TestSupport.Now, TimeSpan.FromSeconds(70));

        delivery.IsDue(TestSupport.Now).Should().BeFalse();
        delivery.IsDue(TestSupport.Now.AddSeconds(60)).Should().BeFalse();
        delivery.IsDue(TestSupport.Now.AddSeconds(66)).Should().BeTrue("the lease expired (with the small clock tolerance)");
    }

    [Fact]
    public void RecordSuccess_FinishesTheDelivery()
    {
        var delivery = New();

        delivery.RecordSuccess(204, 35, "ok", TestSupport.Now);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseCode.Should().Be(204);
        delivery.DeliveredAt.Should().Be(TestSupport.Now);
        delivery.NextAttemptAt.Should().BeNull();
        delivery.IsDue(TestSupport.Now).Should().BeFalse();
    }

    [Fact]
    public void RecordFailure_WithRetry_StaysPending_AndSchedulesTheNextAttempt()
    {
        var delivery = New();
        var retryAt = TestSupport.Now.AddMinutes(1);

        delivery.RecordFailure(503, 120, "HTTP 503", "busy", TestSupport.Now, retryAt);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(1);
        delivery.NextAttemptAt.Should().Be(retryAt);
        delivery.LastError.Should().Be("HTTP 503");
        delivery.IsDue(TestSupport.Now).Should().BeFalse();
        delivery.IsDue(retryAt).Should().BeTrue();
    }

    [Fact]
    public void RecordFailure_WithoutRetry_IsFailed()
    {
        var delivery = New();

        delivery.RecordFailure(400, 10, "HTTP 400", null, TestSupport.Now, retryAt: null);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);
        delivery.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void RecordFailure_ClipsLongErrorsAndSnippets()
    {
        var delivery = New();

        delivery.RecordFailure(500, 1, new string('e', 5000), new string('s', 5000), TestSupport.Now, null);

        delivery.LastError!.Length.Should().Be(WebhookDelivery.ErrorMaxLength);
        delivery.LastResponseSnippet!.Length.Should().Be(WebhookDelivery.SnippetMaxLength);
    }

    [Fact]
    public void Cancel_OnlyAffectsPendingDeliveries()
    {
        var pending = New();
        pending.Cancel("gone");
        var done = New();
        done.RecordSuccess(200, 1, null, TestSupport.Now);
        done.Cancel("gone");

        pending.Status.Should().Be(WebhookDeliveryStatus.Cancelled);
        pending.NextAttemptAt.Should().BeNull();
        done.Status.Should().Be(WebhookDeliveryStatus.Succeeded);
    }

    [Fact]
    public void Redeliver_KeepsTheEventId_AndPointsAtTheOriginal()
    {
        var original = New();
        original.RecordFailure(500, 1, "x", null, TestSupport.Now, null);

        var repeat = original.Redeliver(TestSupport.Now);

        repeat.Id.Should().NotBe(original.Id);
        repeat.EventId.Should().Be(original.EventId, "receivers de-duplicate on the stable webhook-id");
        repeat.EndpointId.Should().Be(original.EndpointId);
        repeat.Payload.Should().Be(original.Payload);
        repeat.RedeliveryOfId.Should().Be(original.Id);
        repeat.Status.Should().Be(WebhookDeliveryStatus.Pending);
        repeat.AttemptCount.Should().Be(0);
    }
}
