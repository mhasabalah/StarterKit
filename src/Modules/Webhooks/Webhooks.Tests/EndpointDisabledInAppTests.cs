using FluentAssertions;
using Webhooks.Domains.Endpoints.EventHandlers;
using Webhooks.Domains.Endpoints.Events;
using Xunit;

namespace Webhooks.Tests;

public class EndpointDisabledInAppTests
{
    [Fact]
    public void The_inbox_entry_is_a_webhooks_warning_in_both_languages()
    {
        var content = WebhookEndpointAutoDisabledEventHandler.InApp(
            new WebhookEndpointAutoDisabledEvent(Guid.NewGuid(), "https://hooks.example.com/x", 7, "timeout"));

        content.Category.Should().Be("webhooks.endpoint-disabled");
        content.Severity.Should().Be(Notification.Contracts.Enums.NotificationSeverity.Warning);
        content.Body.En.Should().Contain("https://hooks.example.com/x").And.Contain("7");
        content.Body.Ar.Should().Contain("https://hooks.example.com/x").And.Contain("7").And.NotBe(content.Body.En);
        content.Title.Ar.Should().NotBe(content.Title.En);
    }
}
