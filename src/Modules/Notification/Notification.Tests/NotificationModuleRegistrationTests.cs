using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Notification.Tests;

/// <summary>The PRODUCTION registration (<see cref="NotificationModule.AddNotificationModule"/>), resolved for real.</summary>
public sealed class NotificationModuleRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:starterkit"] = "Host=localhost;Database=unused;Username=u;Password=p",
            ["Email:AppBaseUrl"] = "https://app.example.com",
            ["Email:BrandName"] = "Acme",
            ["smtp_username"] = "secret-user",
            ["smtp_password"] = "secret-pass"
        };
        foreach (var (key, value) in overrides ?? [])
            settings[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Api.appsettings.json"), optional: false)
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        // Provided by the host (AddHangfire, shared web infrastructure).
        services.AddSingleton(Moq.Mock.Of<Hangfire.IBackgroundJobClient>());
        services.AddSingleton(Moq.Mock.Of<Shared.Core.Web.IHttpContextService>());
        services.AddNotificationModule(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Every_email_template_is_discovered_by_the_assembly_scan()
    {
        using var provider = Build();

        // The registry throws when any EmailTemplate value has no (internal) IEmailTemplate subject class.
        var registry = provider.GetRequiredService<EmailTemplateRegistry>();

        foreach (var template in Enum.GetValues<EmailTemplate>())
        {
            registry.GetModelType(template).Should().NotBeNull($"{template} needs a contracts model");
            registry.GetModelType(template)!.Namespace.Should().Be("Notification.Contracts.Email.Models");
        }
    }

    [Fact]
    public void Brand_name_is_applied_to_the_subjects()
    {
        using var provider = Build();

        provider.GetRequiredService<EmailTemplateRegistry>()
            .ResolveSubject(EmailTemplate.WelcomeUser, NotificationCulture.En).Should().Be("Welcome to Acme");
    }

    [Fact]
    public void Smtp_secrets_overlay_the_email_options()
    {
        using var provider = Build();

        var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
        options.SmtpUsername.Should().Be("secret-user");
        options.SmtpPassword.Should().Be("secret-pass");
        options.AppBaseUrl.Should().Be("https://app.example.com");
    }

    [Fact]
    public void Public_contracts_resolve()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationSender>().Should().BeOfType<NotificationSender>();
        scope.ServiceProvider.GetRequiredService<INotificationLinkResolver>()
            .ToAbsolute("/x").Should().Be("https://app.example.com/x");
        scope.ServiceProvider.GetServices<INotificationChannelAdapter>().Select(a => a.Channel)
            .Should().BeEquivalentTo([NotificationChannel.Email, NotificationChannel.WhatsApp]);
    }

    [Fact]
    public void Every_template_renders_with_the_production_wiring()
    {
        using var provider = Build();

        var failures = NotificationModule.SmokeRenderAllTemplates(
            provider.GetRequiredService<IEmailTemplateRenderer>(),
            provider.GetRequiredService<EmailTemplateSamples>(),
            NullLogger.Instance,
            throwOnFailure: false);

        failures.Should().BeEmpty();
    }

    [Fact]
    public void The_shipped_push_inbox_and_realtime_configuration_is_valid()
    {
        using var provider = Build();

        provider.GetRequiredService<IOptions<PushOptions>>().Value.Provider.Should().Be(PushProvider.Auto);
        provider.GetRequiredService<IOptions<InboxOptions>>().Value.RetentionDays.Should().Be(90);
        NotificationModule.ParseTransports(provider.GetRequiredService<IOptions<RealtimeOptions>>().Value.TransportNames)
            .Should().Be(Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets
                         | Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents
                         | Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling);
    }

    [Fact]
    public void With_apns_enabled_ios_native_tokens_are_routed_to_apns_and_the_rest_to_the_fallback()
    {
        using var provider = Build(new()
        {
            ["Redis:Enabled"] = "false",
            ["Push:Apns:Enabled"] = "true",
            ["Push:Apns:KeyId"] = "ABC123DEFG",
            ["Push:Apns:TeamId"] = "TEAM123456",
            ["Push:Apns:BundleId"] = "com.example.app",
            ["Push:Apns:PrivateKeyFile"] = "/run/secrets/apns.p8"
        });

        provider.GetRequiredService<IPushProvider>().Should().BeOfType<RoutingPushProvider>()
            .Which.Name.Should().Be("NoOp+Apns");
    }

    [Fact]
    public void Without_credentials_Auto_push_resolves_to_NoOp_and_the_delivery_jobs_resolve()
    {
        using var provider = Build(new() { ["Redis:Enabled"] = "false" });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPushProvider>().Should().BeOfType<NoOpPushProvider>();
        scope.ServiceProvider.GetRequiredService<InAppDeliveryJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<PushDeliveryJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<NotificationDailyPurgeJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>().Should().BeOfType<SignalRRealtimeNotifier>();
    }

    [Fact]
    public void Credentials_switch_Auto_push_to_FCM()
    {
        NotificationModule.ResolvePushProvider(new PushOptions { CredentialsFile = "/run/secrets/fcm.json" })
            .Should().Be(PushProvider.Fcm);
        NotificationModule.ResolvePushProvider(new PushOptions { Provider = PushProvider.NoOp, CredentialsJson = "{}" })
            .Should().Be(PushProvider.NoOp, "an explicit NoOp wins");
    }

    [Theory]
    [InlineData("Push:Provider", "Fcm")]
    [InlineData("Push:MaxAttempts", "0")]
    [InlineData("Push:DefaultTimeToLive", "30.00:00:00")]
    [InlineData("Inbox:RetentionDays", "0")]
    [InlineData("Realtime:Transports", "WebSockets,Carrier-Pigeon")]
    [InlineData("Realtime:Transports", "")]
    [InlineData("Realtime:ClientTimeoutInterval", "00:00:05")]
    [InlineData("Notification:DefaultChannelOrder:0", "InApp")]
    public void Invalid_configuration_fails_at_startup(string key, string value)
    {
        using var provider = Build(new() { [key] = value, ["Redis:Enabled"] = "false" });

        var validate = () => provider.GetRequiredService<Microsoft.Extensions.Options.IStartupValidator>().Validate();

        validate.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Realtime_disabled_registers_the_no_op_notifier()
    {
        using var provider = Build(new() { ["Realtime:Enabled"] = "false" });

        provider.GetRequiredService<IRealtimeNotifier>().Should().BeOfType<NoOpRealtimeNotifier>();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Notification.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
