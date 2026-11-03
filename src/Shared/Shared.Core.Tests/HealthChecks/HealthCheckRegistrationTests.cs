using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shared.Core.HealthCheck;
using HealthExtensions = Shared.Core.HealthCheck.Extensions;

namespace Shared.Core.Tests.HealthChecks;

public class HealthCheckRegistrationTests
{
    private const string Db = "Host=localhost;Database=app;Username=u;Password=p";

    private static IReadOnlyCollection<HealthCheckRegistration> Registrations(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomHealthCheck(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations.ToList() ?? [];
    }

    [Fact]
    public void Database_check_uses_the_module_connection_string()
    {
        // Aspire / modules: ConnectionStrings:starterkit. Before the fix the check read an empty PostgresOptions
        // section, so /health never checked the database at all.
        var registrations = Registrations(("ConnectionStrings:starterkit", Db));

        registrations.Should().ContainSingle(r => r.Name == HealthExtensions.DatabaseHealthCheckName)
            .Which.Tags.Should().Contain(HealthCheckTags.Ready).And.NotContain(HealthCheckTags.Live);
        registrations.Should().ContainSingle(r => r.Name == "self")
            .Which.Tags.Should().Contain(HealthCheckTags.Live);
    }

    [Fact]
    public void Database_check_falls_back_to_PostgresOptions()
    {
        var registrations = Registrations(("PostgresOptions:ConnectionString", Db));

        registrations.Should().Contain(r => r.Name == HealthExtensions.DatabaseHealthCheckName);
    }

    [Fact]
    public void Database_connection_string_name_is_configurable()
    {
        var registrations = Registrations(
            ("HealthOptions:DatabaseConnectionStringName", "orders"),
            ("ConnectionStrings:orders", Db),
            ("ConnectionStrings:starterkit", ""));

        registrations.Should().Contain(r => r.Name == HealthExtensions.DatabaseHealthCheckName);
    }

    [Fact]
    public void Without_a_database_only_the_liveness_check_is_registered()
    {
        var registrations = Registrations();

        registrations.Select(r => r.Name).Should().BeEquivalentTo(["self"]);
    }

    [Fact]
    public void Disabled_health_checks_register_nothing()
    {
        var registrations = Registrations(("HealthOptions:Enabled", "false"), ("ConnectionStrings:starterkit", Db));

        registrations.Should().BeEmpty();
    }
}
