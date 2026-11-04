using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MassTransit;
using Npgsql;
using Shared.Messaging.Extensions;
using Testcontainers.PostgreSql;

namespace Shared.Core.Tests.Messaging;

/// <summary>
/// WHO creates the PostgreSql transport schema: app instances only with Database:MigrateOnStartup=true; in production
/// (false) only the explicit one-shot migrator (--migrate-only), which uses Database:MigrationConnectionString.
/// </summary>
public sealed class MassTransitTransportMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _db.StartAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static ServiceCollection Register(Dictionary<string, string?> values)
    {
        values["Messaging:Transport"] = "PostgreSql";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransitWithAssemblies(configuration, typeof(MassTransitTransportMigrationTests).Assembly);
        return services;
    }

    private static bool HasMigrationHostedService(IServiceCollection services) =>
        services.Any(d => d.ServiceType == typeof(IHostedService) &&
                          d.ImplementationType?.Name.Contains("Migration", StringComparison.Ordinal) == true);

    private const string AppConnection = "Host=db;Database=appdb;Username=app;Password=s3cret";

    [Fact]
    public void MigrateOnStartup_true_registers_the_boot_time_transport_migration()
    {
        var services = Register(new() { ["ConnectionStrings:starterkit"] = AppConnection });

        HasMigrationHostedService(services).Should().BeTrue();
    }

    [Fact]
    public void MigrateOnStartup_false_app_instances_do_not_create_transport_infrastructure()
    {
        var services = Register(new()
        {
            ["ConnectionStrings:starterkit"] = AppConnection,
            ["Database:MigrateOnStartup"] = "false"
        });

        HasMigrationHostedService(services).Should().BeFalse();
    }

    [Fact]
    public void MigrateOnly_does_not_rely_on_a_hosted_service_and_uses_the_migration_connection_string_as_admin()
    {
        var services = Register(new()
        {
            ["ConnectionStrings:starterkit"] = AppConnection,
            ["Database:MigrateOnly"] = "true",
            ["Database:MigrationConnectionString"] = "Host=db;Database=appdb;Username=migrator;Password=mig-secret"
        });

        HasMigrationHostedService(services).Should().BeFalse();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SqlTransportOptions>>().Value;
        options.Username.Should().Be("app", "the runtime still connects as the app role");
        options.AdminUsername.Should().Be("migrator");
        options.AdminPassword.Should().Be("mig-secret");
    }

    [Fact]
    public async Task MigrateMessagingTransportAsync_creates_the_transport_schema_for_the_migrate_only_run()
    {
        var builder = new NpgsqlConnectionStringBuilder(_db.GetConnectionString());
        var services = Register(new()
        {
            ["ConnectionStrings:starterkit"] = builder.ConnectionString,
            ["Database:MigrateOnStartup"] = "false",
            ["Database:MigrateOnly"] = "true",
            ["Messaging:PostgreSql:Schema"] = "transport"
        });
        await using var provider = services.BuildServiceProvider();

        (await SchemaExistsAsync(builder)).Should().BeFalse();

        (await provider.MigrateMessagingTransportAsync()).Should().BeTrue();

        (await SchemaExistsAsync(builder)).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateMessagingTransportAsync_is_a_no_op_for_other_transports()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransitWithAssemblies(configuration, typeof(MassTransitTransportMigrationTests).Assembly);
        await using var provider = services.BuildServiceProvider();

        (await provider.MigrateMessagingTransportAsync()).Should().BeFalse();
    }

    private static async Task<bool> SchemaExistsAsync(NpgsqlConnectionStringBuilder builder)
    {
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'transport')", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
