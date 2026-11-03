using System.Net;
using Microsoft.Extensions.Configuration;
using Shared.Core.Extensions;
using Shared.Core.Options;

namespace Shared.Core.Tests.Caching;

public class RedisConnectionConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static DnsEndPoint SingleEndpoint(StackExchange.Redis.ConfigurationOptions options) =>
        options.EndPoints.Should().ContainSingle().Which.Should().BeOfType<DnsEndPoint>().Subject;

    [Fact]
    public void Connection_string_wins_over_host_and_port()
    {
        // Aspire injects ConnectionStrings__redis; the Redis section still says "redis:6379" (compose default).
        var config = Config(
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("ConnectionStrings:redis", "cache.internal:6390,password=s3cret,ssl=true"));

        var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { Host = "redis", Port = 6379 });

        var endpoint = SingleEndpoint(options);
        endpoint.Host.Should().Be("cache.internal");
        endpoint.Port.Should().Be(6390);
        options.Password.Should().Be("s3cret");
        options.Ssl.Should().BeTrue();
    }

    [Fact]
    public void Client_behaviour_comes_from_the_Redis_section_even_with_a_connection_string()
    {
        var config = Config(
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("ConnectionStrings:redis", "localhost:6379,abortConnect=true,connectTimeout=1"));

        var options = RedisConnectionConfiguration.Resolve(
            config, new RedisOptions { AbortOnConnectFail = false, ConnectTimeout = 4000, SyncTimeout = 3000 });

        options.AbortOnConnectFail.Should().BeFalse("a Redis outage must degrade the cache, not fail startup");
        options.ConnectTimeout.Should().Be(4000);
        options.SyncTimeout.Should().Be(3000);
        options.AsyncTimeout.Should().Be(3000);
    }

    [Fact]
    public void Production_with_a_connection_string_does_not_need_the_secret_file()
    {
        var config = Config(
            ("ASPNETCORE_ENVIRONMENT", "Production"),
            ("ConnectionStrings:redis", "redis:6379,password=from-secret-provider"));

        var options = RedisConnectionConfiguration.Resolve(
            config, new RedisOptions { PasswordSecretPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });

        options.Password.Should().Be("from-secret-provider");
    }

    [Fact]
    public void Development_uses_appsettings_host_and_ignores_container_overrides()
    {
        var config = Config(
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("REDIS_HOST", "redis"),
            ("REDIS_PORT", "7000"));

        var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { Host = "localhost", Port = 6379 });

        var endpoint = SingleEndpoint(options);
        endpoint.Host.Should().Be("localhost");
        endpoint.Port.Should().Be(6379);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("Staging")]
    public void Container_environments_take_REDIS_HOST_and_REDIS_PORT(string environment)
    {
        var config = Config(
            ("ASPNETCORE_ENVIRONMENT", environment),
            ("REDIS_HOST", "redis"),
            ("REDIS_PORT", "6380"));

        var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { Host = "localhost", Port = 6379 });

        var endpoint = SingleEndpoint(options);
        endpoint.Host.Should().Be("redis");
        endpoint.Port.Should().Be(6380);
    }

    [Fact]
    public void Non_production_password_prefers_REDIS_PASSWORD_over_appsettings()
    {
        var config = Config(("ASPNETCORE_ENVIRONMENT", "Development"), ("REDIS_PASSWORD", "from-env"));

        var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { Password = "from-appsettings" });

        options.Password.Should().Be("from-env");
    }

    [Fact]
    public void Non_production_without_any_password_connects_without_one()
    {
        var config = Config(("ASPNETCORE_ENVIRONMENT", "Development"));

        var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { Password = "" });

        options.Password.Should().BeNull();
    }

    [Fact]
    public void Production_reads_and_trims_the_secret_file()
    {
        var secretFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(secretFile, "  prod-secret \n");
            var config = Config(("ASPNETCORE_ENVIRONMENT", "Production"), ("REDIS_PASSWORD", "must-be-ignored"));

            var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { PasswordSecretPath = secretFile });

            options.Password.Should().Be("prod-secret");
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Fact]
    public void Missing_environment_is_treated_as_production_and_fails_fast_without_the_secret()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var act = () => RedisConnectionConfiguration.Resolve(Config(), new RedisOptions { PasswordSecretPath = missing });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missing}*");
    }

    [Fact]
    public void Production_with_an_empty_secret_file_fails_fast()
    {
        var secretFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(secretFile, "   ");
            var config = Config(("ASPNETCORE_ENVIRONMENT", "Production"));

            var act = () => RedisConnectionConfiguration.Resolve(config, new RedisOptions { PasswordSecretPath = secretFile });

            act.Should().Throw<InvalidOperationException>().WithMessage("*empty*");
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Theory]
    [InlineData("", 6379)]
    [InlineData("localhost", 0)]
    [InlineData("localhost", 70000)]
    public void Invalid_host_or_port_fails_with_a_clear_message(string host, int port)
    {
        var config = Config(("ASPNETCORE_ENVIRONMENT", "Development"));

        var act = () => RedisConnectionConfiguration.Resolve(config, new RedisOptions { Host = host, Port = port });

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:redis*");
    }

    [Fact]
    public void Non_default_database_and_tls_are_applied_from_options()
    {
        var config = Config(("ASPNETCORE_ENVIRONMENT", "Development"));

        var options = RedisConnectionConfiguration.Resolve(config, new RedisOptions { Database = 3, UseSsl = true });

        options.DefaultDatabase.Should().Be(3);
        options.Ssl.Should().BeTrue();
    }
}
