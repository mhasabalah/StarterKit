using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Notification.Tests;

/// <summary>The PRODUCTION registration (<see cref="NotificationModule.AddNotificationModule"/>), resolved for real.</summary>
public sealed class NotificationModuleRegistrationTests
{
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Api.appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = "Host=localhost;Database=unused;Username=u;Password=p",
                ["Email:AppBaseUrl"] = "https://app.example.com",
                ["Email:BrandName"] = "Acme",
                ["smtp_username"] = "secret-user",
                ["smtp_password"] = "secret-pass"
            })
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

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Notification.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
