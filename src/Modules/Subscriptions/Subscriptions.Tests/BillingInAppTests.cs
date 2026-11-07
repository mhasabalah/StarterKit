using FluentAssertions;
using Moq;
using Notification.Contracts;
using Notification.Contracts.InApp;
using Shared.Core.MultiTenancy;
using Notification.Contracts.Enums;
using Subscriptions.Domains.Subscriptions.Services;
using Xunit;

namespace Subscriptions.Tests;

public class BillingInAppTests
{
    public static IEnumerable<object[]> EventKeys() =>
        new[]
        {
            "subscription-activated", "past-due", "expired", "renewed", "payment-rejected", "subscription-rejected",
            "refund-approved", "refund-processed", "refund-rejected", "cancellation-scheduled", "cancelled",
            "downgrade-scheduled", "downgrade-cancelled", "downgrade-applied", "upgraded", "upgrade-reverted",
            "invoice-ready", "trial-ending"
        }.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(EventKeys))]
    public void Every_billing_event_has_billing_category_and_real_text_in_both_languages(string key)
    {
        var content = BillingInApp.Content(key, NotificationSeverity.Info, BillingInApp.SubscriptionUrl,
            args: ["Pro", "2027-01-01", "Basic"]);

        content.Category.Should().Be($"billing.{key}");
        foreach (var text in new[] { content.Title, content.Body })
        {
            text.En.Should().NotBeNullOrWhiteSpace().And.NotStartWith("InApp.").And.NotContain("{");
            text.Ar.Should().NotBeNullOrWhiteSpace().And.NotStartWith("InApp.").And.NotContain("{");
            text.Ar.Should().NotBe(text.En);
        }
    }

    [Fact]
    public void Arguments_are_filled_into_both_languages()
    {
        var content = BillingInApp.Content("renewed", NotificationSeverity.Success, BillingInApp.SubscriptionUrl,
            args: ["Pro", "2027-01-01"]);

        content.Body.En.Should().Contain("Pro").And.Contain("2027-01-01");
        content.Body.Ar.Should().Contain("Pro").And.Contain("2027-01-01");
    }

    // ---- wiring: push flags, tenant scope ----------------------------------------------------------------------

    private static (Mock<INotificationBuilder> Builder, List<string> Calls) Recording(Action? onSend = null)
    {
        var calls = new List<string>();
        var builder = new Mock<INotificationBuilder>();
        builder.Setup(b => b.WithInApp(It.IsAny<InAppContent>())).Returns(builder.Object)
            .Callback<InAppContent>(c => calls.Add($"inapp:{c.Category}"));
        builder.Setup(b => b.WithPush(It.IsAny<bool>())).Returns(builder.Object)
            .Callback<bool>(sensitive => calls.Add($"push:{sensitive}"));
        builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                onSend?.Invoke();
                return Task.FromResult(Guid.NewGuid());
            });
        return (builder, calls);
    }

    [Theory]
    [InlineData(false, false, "inapp:billing.renewed")]
    [InlineData(true, false, "inapp:billing.renewed|push:False")]
    [InlineData(true, true, "inapp:billing.renewed|push:True")]
    public void WithBillingInApp_adds_the_inbox_entry_and_a_push_only_when_asked(bool push, bool sensitive, string expected)
    {
        var (builder, calls) = Recording();

        builder.Object.WithBillingInApp("renewed", NotificationSeverity.Success, BillingInApp.SubscriptionUrl,
            push, sensitive, args: ["Pro", "2027-01-01"]);

        string.Join("|", calls).Should().Be(expected);
    }

    private sealed class FixedTenants(TenantInfo? tenant, bool multiTenant = true) : ITenantScopeFactory
    {
        public bool IsMultiTenant => multiTenant;

        public Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(tenant);
    }

    [Fact]
    public async Task The_send_happens_inside_the_subscribers_tenant_scope_and_leaves_it_afterwards()
    {
        var tenant = new TenantInfo(Guid.NewGuid(), "acme", "Acme");
        TenantInfo? seenDuringSend = null;
        var (builder, _) = Recording(() => seenDuringSend = TenantScope.Current?.Tenant);

        await builder.Object.SendForSubscriberAsync(new FixedTenants(tenant), tenant.Id, NotificationChannel.Email, CancellationToken.None);

        seenDuringSend.Should().Be(tenant);
        TenantScope.Current?.Tenant.Should().NotBe(tenant);
    }

    [Fact]
    public async Task Without_a_tenant_to_enter_the_send_runs_in_the_ambient_scope()
    {
        var (builder, _) = Recording();

        await builder.Object.SendForSubscriberAsync(new FixedTenants(null, multiTenant: false), Guid.NewGuid(),
            NotificationChannel.Email, CancellationToken.None);

        builder.Verify(b => b.SendAsync(NotificationChannel.Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void The_client_payload_carries_the_tenant_id_only_in_multi_tenant_mode()
    {
        var subscriber = Guid.NewGuid();
        var subscription = Guid.NewGuid();

        BillingInApp.SubscriptionData(subscription, subscriber, new FixedTenants(null, multiTenant: true))
            .Should().Contain("tenantId", subscriber.ToString()).And.Contain("subscriptionId", subscription.ToString());
        BillingInApp.SubscriptionData(subscription, subscriber, new FixedTenants(null, multiTenant: false))
            .Should().NotContainKey("tenantId");
    }
}
