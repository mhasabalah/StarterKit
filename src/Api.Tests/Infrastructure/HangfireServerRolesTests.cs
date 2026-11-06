using Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.Tests.Infrastructure;

/// <summary>
/// <c>Hangfire:Servers:&lt;Name&gt;</c>: the same image runs as API-only or worker-only replicas
/// (docker-compose.worker.yml). Nothing connects: only the registrations are inspected.
/// </summary>
public sealed class HangfireServerRolesTests
{
    private static IConfiguration Config(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))
                .Append(new KeyValuePair<string, string?>("ConnectionStrings:starterkit", "Host=localhost;Database=x")))
            .Build();

    private static int ProcessingServers(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHangfireServices(configuration);
        return services.Count(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void By_default_every_server_is_on_with_its_worker_count()
    {
        var roles = HangfireServerRoles.From(Config());

        roles.Should().BeEquivalentTo(new
        {
            Default = new { Enabled = true, WorkerCount = 5 },
            Notification = new { Enabled = true, WorkerCount = 4 },
            DataTransfer = new { Enabled = true, WorkerCount = 2 }
        });
    }

    [Fact]
    public void Settings_override_one_server_and_keep_the_other_defaults()
    {
        var roles = HangfireServerRoles.From(Config(
            ("Hangfire:Servers:DataTransfer:Enabled", "false"),
            ("Hangfire:Servers:Notification:WorkerCount", "8")));

        roles.DataTransfer.Enabled.Should().BeFalse();
        roles.DataTransfer.WorkerCount.Should().Be(2);
        roles.Notification.WorkerCount.Should().Be(8);
        roles.Default.Enabled.Should().BeTrue();
    }

    [Fact]
    public void A_worker_count_below_one_fails_at_startup()
    {
        var act = () => HangfireServerRoles.From(Config(("Hangfire:Servers:DataTransfer:WorkerCount", "0")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*WorkerCount*");
    }

    [Fact]
    public void Disabled_servers_are_not_registered_so_an_api_or_worker_role_runs_only_its_queues()
    {
        var all = ProcessingServers(Config());
        var api = ProcessingServers(Config(("Hangfire:Servers:DataTransfer:Enabled", "false")));
        var worker = ProcessingServers(Config(
            ("Hangfire:Servers:Default:Enabled", "false"),
            ("Hangfire:Servers:Notification:Enabled", "false")));
        var none = ProcessingServers(Config(
            ("Hangfire:Servers:Default:Enabled", "false"),
            ("Hangfire:Servers:Notification:Enabled", "false"),
            ("Hangfire:Servers:DataTransfer:Enabled", "false")));

        (all - none).Should().Be(3, "default, notification and data-transfer each add one processing server");
        (all - api).Should().Be(1);
        (worker - none).Should().Be(1);
    }
}
