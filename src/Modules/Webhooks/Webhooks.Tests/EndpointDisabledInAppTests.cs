using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Notification.Contracts.Push;
using Shared.Contracts.Mediation;
using Shared.Core.MultiTenancy;
using Tenancy.Contracts.Tenants;
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
            new WebhookEndpointAutoDisabledEvent(Guid.NewGuid(), "https://hooks.example.com/x?token=SECRET", 7, "timeout"));

        content.Category.Should().Be("webhooks.endpoint-disabled");
        content.Severity.Should().Be(Notification.Contracts.Enums.NotificationSeverity.Warning);
        content.Body.En.Should().Contain("hooks.example.com").And.Contain("7").And.NotContain("SECRET").And.NotContain("/x");
        content.Body.Ar.Should().Contain("hooks.example.com").And.Contain("7").And.NotContain("SECRET").And.NotBe(content.Body.En);
        content.Title.Ar.Should().NotBe(content.Title.En);
    }

    [Theory]
    [InlineData("https://hooks.example.com/a/b?token=1", "hooks.example.com")]
    [InlineData("not a url", "-")]
    public void Only_the_host_of_the_endpoint_is_shown(string url, string expected) =>
        WebhookEndpointAutoDisabledEventHandler.HostOf(url).Should().Be(expected);

    [Fact]
    public async Task The_owner_gets_email_in_app_and_a_SENSITIVE_push()
    {
        var tenantId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetTenantByIdQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TenantDto(
            tenantId, "acme", "Acme", null, default, default, null, null, null, null, null, null, owner, DateTime.UtcNow, 1));
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        var calls = new List<string>();
        var builder = new Mock<INotificationBuilder>();
        builder.Setup(b => b.WithEmail(It.IsAny<EmailTemplate>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<EmailAttachment[]>()))
            .Returns(builder.Object).Callback(() => calls.Add("email"));
        builder.Setup(b => b.WithInApp(It.IsAny<InAppContent>())).Returns(builder.Object)
            .Callback<InAppContent>(c => calls.Add($"inapp:{c.Category}"));
        builder.Setup(b => b.WithPush(It.IsAny<bool>())).Returns(builder.Object).Callback<bool>(x => calls.Add($"push:{x}"));
        builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var notifications = new Mock<INotificationSender>();
        notifications.Setup(n => n.For(owner, It.IsAny<NotificationCulture>())).Returns(builder.Object);

        await new WebhookEndpointAutoDisabledEventHandler(sender.Object, notifications.Object, tenantContext.Object,
                NullLogger<WebhookEndpointAutoDisabledEventHandler>.Instance)
            .Handle(new WebhookEndpointAutoDisabledEvent(Guid.NewGuid(), "https://hooks.example.com/x", 5, "boom"), default);

        calls.Should().Equal("email", "inapp:webhooks.endpoint-disabled", "push:True");
    }
}
