using System.Xml.Linq;
using FluentValidation.TestHelper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Shared.Core.MultiTenancy;
using Tenancy.Contracts.Tenants;
using Webhooks.Contracts.Endpoints;
using Webhooks.Domains.Endpoints.EventHandlers;
using Webhooks.Domains.Endpoints.Events;
using Webhooks.Domains.Endpoints.Features;
using Webhooks.Options;

namespace Webhooks.Tests.Features;

public class WebhookValidatorAndHandlerTests
{
    private static CreateWebhookEndpointCommandValidator Validator(Action<WebhooksOptions>? configure = null) =>
        new(TestSupport.Localizer(), TestSupport.UrlPolicy(configure), TestSupport.Catalog());

    private static CreateWebhookEndpointCommand Valid() =>
        new("https://hooks.example.com/in", "d", ["catalog.product-published.v1"]);

    [Fact]
    public void A_valid_create_command_passes() => Validator().TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("http://hooks.example.com/in")]
    [InlineData("https://127.0.0.1/in")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://user:secret@hooks.example.com/in")]
    [InlineData("ftp://hooks.example.com")]
    [InlineData("nope")]
    public void Create_RejectsUnsafeOrInvalidUrls(string url) =>
        Validator().TestValidate(Valid() with { Url = url }).ShouldHaveValidationErrorFor(x => x.Url);

    [Fact]
    public void Create_RejectsUnknownEventTypes() =>
        Validator().TestValidate(Valid() with { EventTypes = ["catalog.product-published.v1", "made.up.v1"] })
            .ShouldHaveValidationErrorFor(x => x.EventTypes);

    [Fact]
    public void Create_AcceptsTheWildcard() =>
        Validator().TestValidate(Valid() with { EventTypes = ["*"] }).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_RequiresAtLeastOneEventType() =>
        Validator().TestValidate(Valid() with { EventTypes = [] }).ShouldHaveValidationErrorFor(x => x.EventTypes);

    [Fact]
    public void Create_RejectsTooLongDescriptions() =>
        Validator().TestValidate(Valid() with { Description = new string('d', 501) })
            .ShouldHaveValidationErrorFor(x => x.Description);

    [Fact]
    public void Validation_messages_are_localized_to_arabic()
    {
        using var _ = new UiCulture("ar");

        var result = Validator().TestValidate(Valid() with { Url = "http://hooks.example.com/in" });

        result.Errors.Single(e => e.PropertyName == "Url").ErrorMessage.Should().Contain("https").And.MatchRegex("[\\u0600-\\u06FF]");
    }

    [Fact]
    public void Update_RejectsAnEmptyIdAndANegativeVersion()
    {
        var validator = new UpdateWebhookEndpointCommandValidator(TestSupport.Localizer(), TestSupport.UrlPolicy(),
            TestSupport.Catalog());

        var result = validator.TestValidate(new UpdateWebhookEndpointCommand(Guid.Empty, "https://a.example.com", null,
            ["*"], true, -1));

        result.ShouldHaveValidationErrorFor(x => x.Id);
        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Fact]
    public void Every_resource_key_exists_in_both_English_and_Arabic()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Webhooks", "Resources");
        var en = Keys(Path.Combine(directory, "WebhooksResource.resx"));
        var ar = Keys(Path.Combine(directory, "WebhooksResource.ar.resx"));

        en.Should().NotBeEmpty();
        ar.Should().BeEquivalentTo(en);

        static HashSet<string> Keys(string path) =>
            XDocument.Load(path).Descendants("data").Select(d => d.Attribute("name")!.Value).ToHashSet();
    }

    // ----- production guard for the SSRF switches ---------------------------------------------------------------

    private static ValidateOptionsResult ValidateIn(string? environment, Action<WebhooksOptions> configure)
    {
        var services = new ServiceCollection();
        if (environment is not null)
        {
            var host = new Mock<IHostEnvironment>();
            host.SetupGet(h => h.EnvironmentName).Returns(environment);
            services.AddSingleton(host.Object);
        }

        var options = new WebhooksOptions();
        configure(options);
        return new WebhooksOptionsValidator(services.BuildServiceProvider()).Validate(null, options);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_development_private_networks_and_plain_http_are_refused(string environment)
    {
        ValidateIn(environment, o => o.AllowPrivateNetworks = true).Failed.Should().BeTrue();
        ValidateIn(environment, o => o.RequireHttps = false).Failed.Should().BeTrue();
        ValidateIn(environment, _ => { }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData(null)]
    public void In_development_and_tests_the_switches_are_allowed(string? environment) =>
        ValidateIn(environment, o => { o.AllowPrivateNetworks = true; o.RequireHttps = false; }).Succeeded.Should().BeTrue();

    // ----- auto-disable notification --------------------------------------------------------------------------------

    private sealed class FakeNotifications : INotificationSender, INotificationBuilder
    {
        public Guid? ToUser;
        public EmailTemplate? Template;
        public Dictionary<string, object>? Model;
        public NotificationChannel? Channel;
        public int Sent;

        public INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En)
        {
            ToUser = userId;
            return this;
        }

        public INotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En, Guid? userId = null) => this;

        public INotificationBuilder WithEmail(EmailTemplate template, Dictionary<string, object>? model = null,
            EmailAttachment[]? attachments = null)
        {
            Template = template;
            Model = model;
            return this;
        }

        public INotificationBuilder WithWhatsApp(WhatsAppTemplate template, Dictionary<string, object>? model = null) => this;

        public INotificationBuilder WithInApp(Notification.Contracts.InApp.InAppContent content) => this;

        public INotificationBuilder WithPush(Notification.Contracts.Push.PushContent content) => this;

        public INotificationBuilder WithPush(bool sensitive = false) => this;

        public Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default)
        {
            Sent++;
            Channel = forcedChannel;
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private static WebhookEndpointAutoDisabledEvent Event() =>
        new(Guid.NewGuid(), "https://hooks.example.com/in", 10, "HTTP 500");

    private static ITenantContext TenantContextOf(Guid? tenantId)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(c => c.TenantId).Returns(tenantId);
        return context.Object;
    }

    [Fact]
    public async Task AutoDisabled_EmailsTheTenantOwner_ForcedToEmail()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.Is<GetTenantByIdQuery>(q => q.TenantId == tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantDto(tenantId, "acme", "Acme Ltd", null, default, default, null, null, null, null, null, null,
                ownerId, TestSupport.Now, 1));
        var notifications = new FakeNotifications();
        var handler = new WebhookEndpointAutoDisabledEventHandler(sender.Object, notifications, TenantContextOf(tenantId),
            NullLogger<WebhookEndpointAutoDisabledEventHandler>.Instance);

        await handler.Handle(Event(), CancellationToken.None);

        notifications.Sent.Should().Be(1);
        notifications.ToUser.Should().Be(ownerId);
        notifications.Template.Should().Be(EmailTemplate.WebhookEndpointDisabled);
        notifications.Channel.Should().Be(NotificationChannel.Email);
        notifications.Model!["TenantName"].Should().Be("Acme Ltd");
        notifications.Model["EndpointAddress"].Should().Be("https://hooks.example.com/in");
        notifications.Model["ConsecutiveFailures"].Should().Be(10);
    }

    [Fact]
    public async Task AutoDisabled_WithoutATenant_SendsNothing()
    {
        var notifications = new FakeNotifications();
        var handler = new WebhookEndpointAutoDisabledEventHandler(Mock.Of<ISender>(), notifications, TenantContextOf(null),
            NullLogger<WebhookEndpointAutoDisabledEventHandler>.Instance);

        await handler.Handle(Event(), CancellationToken.None);

        notifications.Sent.Should().Be(0);
    }

    [Fact]
    public async Task AutoDisabled_WhenTheMailFails_DoesNotThrow()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetTenantByIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tenancy down"));
        var handler = new WebhookEndpointAutoDisabledEventHandler(sender.Object, new FakeNotifications(),
            TenantContextOf(Guid.NewGuid()), NullLogger<WebhookEndpointAutoDisabledEventHandler>.Instance);

        var act = () => handler.Handle(Event(), CancellationToken.None);

        await act.Should().NotThrowAsync("losing a courtesy mail must not fail the delivery bookkeeping");
    }
}
