using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Messaging.Extensions;

namespace Shared.Core.Tests.Messaging;

/// <summary>
/// <c>Messaging:Transport</c> selection. The bus is built (consumer/transport configuration runs) but never started,
/// so no broker or database is needed.
/// </summary>
public sealed class MassTransitTransportTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransitWithAssemblies(configuration, typeof(MassTransitTransportTests).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Default_transport_is_in_memory()
    {
        await using var provider = Build(Config());

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.Transport.Should().Be(MessagingTransport.InMemory);
    }

    [Fact]
    public async Task InMemory_transport_builds_the_bus()
    {
        await using var provider = Build(Config(("Messaging:Transport", "InMemory")));

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IPublishEndpoint>().Should().NotBeNull();
    }

    [Fact]
    public async Task PostgreSql_transport_builds_the_bus_and_takes_its_options_from_the_connection_string()
    {
        var configuration = Config(
            ("Messaging:Transport", "PostgreSql"),
            ("Messaging:PostgreSql:Schema", "mt_transport"),
            ("Messaging:PostgreSql:Role", "mt_role"),
            ("Messaging:PostgreSql:CreateInfrastructure", "false"),
            ("ConnectionStrings:starterkit", "Host=db.example.com;Port=6543;Database=appdb;Username=app;Password=s3cret"));

        await using var provider = Build(configuration);

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
        var options = provider.GetRequiredService<IOptions<SqlTransportOptions>>().Value;
        options.Host.Should().Be("db.example.com");
        options.Port.Should().Be(6543);
        options.Database.Should().Be("appdb");
        options.Username.Should().Be("app");
        options.Password.Should().Be("s3cret");
        options.Schema.Should().Be("mt_transport");
        options.Role.Should().Be("mt_role");
        options.AdminUsername.Should().Be("app", "the admin defaults to the connection-string user");
        options.AdminPassword.Should().Be("s3cret");
    }

    [Fact]
    public async Task PostgreSql_transport_uses_a_dedicated_admin_user_when_configured()
    {
        var configuration = Config(
            ("Messaging:Transport", "PostgreSql"),
            ("Messaging:PostgreSql:AdminUsername", "migrator"),
            ("Messaging:PostgreSql:AdminPassword", "admin-secret"),
            ("ConnectionStrings:starterkit", "Host=localhost;Database=appdb;Username=app;Password=s3cret"));

        await using var provider = Build(configuration);

        var options = provider.GetRequiredService<IOptions<SqlTransportOptions>>().Value;
        options.Username.Should().Be("app");
        options.AdminUsername.Should().Be("migrator");
        options.AdminPassword.Should().Be("admin-secret");
    }

    [Fact]
    public async Task PostgreSql_transport_falls_back_to_the_PostgresOptions_connection_string()
    {
        var configuration = Config(
            ("Messaging:Transport", "PostgreSql"),
            ("Messaging:PostgreSql:CreateInfrastructure", "false"),
            ("PostgresOptions:ConnectionString", "Host=fallback.example.com;Database=fallbackdb;Username=u;Password=p"));

        await using var provider = Build(configuration);

        provider.GetRequiredService<IOptions<SqlTransportOptions>>().Value.Host.Should().Be("fallback.example.com");
    }

    [Fact]
    public void PostgreSql_transport_without_a_connection_string_fails_fast()
    {
        var act = () => Build(Config(("Messaging:Transport", "PostgreSql")));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("ConnectionStrings:starterkit");
    }

    [Fact]
    public async Task RabbitMq_transport_builds_the_bus_from_the_explicit_settings()
    {
        var configuration = Config(
            ("Messaging:Transport", "RabbitMq"),
            ("Messaging:RabbitMq:Host", "rabbit.example.com"),
            ("Messaging:RabbitMq:Username", "app"),
            ("Messaging:RabbitMq:Password", "pw"));

        await using var provider = Build(configuration);

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        options.Transport.Should().Be(MessagingTransport.RabbitMq);
        options.RabbitMq.Host.Should().Be("rabbit.example.com");
        options.RabbitMq.Port.Should().Be(5672);
        options.RabbitMq.VirtualHost.Should().Be("/");
    }

    [Fact]
    public async Task RabbitMq_transport_builds_the_bus_from_an_amqp_connection_string()
    {
        var configuration = Config(
            ("Messaging:Transport", "RabbitMq"),
            ("ConnectionStrings:rabbitmq", "amqp://user:pass@rabbit.example.com:5673/"));

        await using var provider = Build(configuration);

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
    }

    [Fact]
    public void RabbitMq_transport_without_any_host_fails_fast()
    {
        var act = () => Build(Config(("Messaging:Transport", "RabbitMq"), ("Messaging:RabbitMq:Host", "")));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("rabbitmq");
    }

    [Fact]
    public void Unknown_transport_value_is_rejected()
    {
        var act = () => Build(Config(("Messaging:Transport", "99")));

        act.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Contain("not supported").And.Contain("InMemory | PostgreSql | RabbitMq");
    }

    [Fact]
    public void Misspelled_transport_name_is_rejected()
    {
        var act = () => Build(Config(("Messaging:Transport", "Kafka")));

        act.Should().Throw<InvalidOperationException>();
    }
}
