using AppHost;
using Microsoft.Extensions.Configuration;

namespace AppHost.Tests;

public class AppHostSettingsTests
{
    private static AppHostSettings From(params (string Key, string? Value)[] values) =>
        AppHostSettings.From(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build());

    [Fact]
    public void Defaults_are_the_lean_dev_setup()
    {
        From().Should().Be(new AppHostSettings(ObservabilityFullStack: false, MessagingTransport.InMemory, PostgresHostPort: null));
    }

    [Theory]
    [InlineData("rabbitmq", MessagingTransport.RabbitMq)]
    [InlineData("PostgreSql", MessagingTransport.PostgreSql)]
    [InlineData("INMEMORY", MessagingTransport.InMemory)]
    public void Transport_is_case_insensitive(string value, MessagingTransport expected)
    {
        From((AppHostSettings.TransportKey, value)).Transport.Should().Be(expected);
    }

    [Theory]
    [InlineData("Kafka")]
    [InlineData("7")]
    public void Unknown_transport_fails_fast(string value)
    {
        var act = () => From((AppHostSettings.TransportKey, value));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{AppHostSettings.TransportKey}*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("abc")]
    public void Invalid_postgres_port_fails_fast(string value)
    {
        var act = () => From((AppHostSettings.PostgresHostPortKey, value));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{AppHostSettings.PostgresHostPortKey}*");
    }

    [Fact]
    public void Empty_postgres_port_means_random()
    {
        From((AppHostSettings.PostgresHostPortKey, "")).PostgresHostPort.Should().BeNull();
    }

    [Fact]
    public void Full_stack_switch_is_read()
    {
        From((AppHostSettings.ObservabilityFullStackKey, "true")).ObservabilityFullStack.Should().BeTrue();
    }

    [Fact]
    public void ClamAv_switch_is_read_and_off_by_default()
    {
        From().ClamAv.Should().BeFalse();
        From((AppHostSettings.ClamAvKey, "true")).ClamAv.Should().BeTrue();
    }
}
