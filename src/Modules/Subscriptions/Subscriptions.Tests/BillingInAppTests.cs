using FluentAssertions;
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
}
