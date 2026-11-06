using Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Api.Tests.Infrastructure;

public sealed class ProductionConfigurationGuardTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Theory]
    [InlineData("Production")]
    [InlineData("Docker")]
    [InlineData("Staging")]
    public void Throws_outside_dev_when_placeholder_remains(string environment)
    {
        var config = Config(("Jwt:SecretKey", "CHANGE_ME_TO_A_RANDOM_SECRET"));

        var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, environment);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SecretKey*");
    }

    [Fact]
    public void Rotation_keys_are_covered_a_placeholder_previous_key_blocks_startup()
    {
        var config = Config(
            ("JwtSettings:SecretKey", "a-real-secret-from-docker-secrets-0123456789"),
            ("JwtSettings:PreviousSecretKeys:0", "CHANGE_ME_OLD_KEY"));

        var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*JwtSettings:PreviousSecretKeys:0*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("development")]
    public void Tolerates_placeholders_in_dev_and_test(string environment)
    {
        var config = Config(("Jwt:SecretKey", "CHANGE_ME"));

        var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, environment);

        act.Should().NotThrow();
    }

    [Fact]
    public void Error_message_lists_keys_but_never_leaks_values()
    {
        var config = Config(
            ("ConnectionStrings:starterkit", "Host=db;Password=CHANGE_ME;Username=real-user-name"),
            ("Hangfire:Dashboard:Password", "change_me"));

        var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, "Production");

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("ConnectionStrings:starterkit").And.Contain("Hangfire:Dashboard:Password");
        ex.Message.Should().NotContain("real-user-name").And.NotContain("Host=db");
    }

    [Fact]
    public void Passes_when_all_placeholders_are_overridden()
    {
        // Later providers (env vars / Docker secrets) override appsettings. Only the final value counts.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Jwt:SecretKey", "CHANGE_ME")])
            .AddInMemoryCollection([new("Jwt:SecretKey", "a-real-secret-from-docker-secrets-0123456789")])
            .Build();

        var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void Shipped_appsettings_contain_placeholders_so_the_guard_is_meaningful()
    {
        // Guards the guard: if someone commits real secrets into appsettings.json, this fails first.
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        File.Exists(path).Should().BeTrue("Api's appsettings.json is copied to the test output");

        var config = new ConfigurationBuilder().AddJsonFile(path).Build();

        ProductionConfigurationGuard.FindPlaceholders(config)
            .Should().Contain(k => k.StartsWith("ConnectionStrings:", StringComparison.Ordinal))
            .And.Contain(k => k.Contains("SecretKey", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Docker", "InMemory")]
    [InlineData("Staging", "inmemory")]
    public void Refuses_in_memory_messaging_outside_dev(string environment, string? transport)
    {
        var config = Config(("Messaging:Transport", transport));

        var act = () => ProductionConfigurationGuard.EnsureDurableMessaging(config, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*InMemory*");
    }

    [Theory]
    [InlineData("Production", "PostgreSql")]
    [InlineData("Docker", "RabbitMq")]
    [InlineData("Development", "InMemory")]
    [InlineData("Testing", null)]
    public void Allows_durable_transport_or_relaxed_environment(string environment, string? transport)
    {
        var config = Config(("Messaging:Transport", transport));

        var act = () => ProductionConfigurationGuard.EnsureDurableMessaging(config, environment);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Docker", null)]
    [InlineData("Production", "CHANGE_ME")]
    public void BotProtection_enabled_without_a_real_secret_blocks_startup(string environment, string? secret)
    {
        var config = Config(("BotProtection:Enabled", "true"), ("BotProtection:SecretKey", secret));

        var act = () => ProductionConfigurationGuard.EnsureBotProtectionConfigured(config, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*BotProtection:SecretKey*");
    }

    [Theory]
    [InlineData("Production", "true", "a-real-secret")]
    [InlineData("Production", "false", "")]
    [InlineData("Development", "true", "")]
    public void BotProtection_is_allowed_when_disabled_configured_or_relaxed(string environment, string enabled, string secret)
    {
        var config = Config(("BotProtection:Enabled", enabled), ("BotProtection:SecretKey", secret));

        var act = () => ProductionConfigurationGuard.EnsureBotProtectionConfigured(config, environment);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Development", "Live", "sk_test_abc")]
    [InlineData("Production", "Live", "sk_test_abc")]
    [InlineData("Development", "Sandbox", "sk_live_abc")]
    [InlineData("Production", null, "sk_live_abc")]
    public void Payment_gateway_key_must_match_its_mode_in_every_environment(string environment, string? mode, string apiKey)
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Stripe:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Stripe:Mode", mode),
            ("Subscriptions:PaymentGateways:Stripe:ApiKey", apiKey));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Stripe*");
    }

    [Theory]
    [InlineData("Production", "Sandbox", "sk_test_abc", false)]
    [InlineData("Production", null, "sk_test_abc", false)]
    public void Sandbox_gateway_is_refused_in_production_unless_explicitly_allowed(string environment, string? mode, string apiKey, bool allowed)
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Stripe:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Stripe:Mode", mode),
            ("Subscriptions:PaymentGateways:Stripe:ApiKey", apiKey),
            ("Subscriptions:PaymentGateways:Stripe:AllowSandboxInProduction", allowed.ToString()));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*sandbox*");
    }

    [Fact]
    public void Sandbox_gateway_in_production_is_accepted_with_the_explicit_opt_in()
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Stripe:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Stripe:ApiKey", "sk_test_abc"),
            ("Subscriptions:PaymentGateways:Stripe:AllowSandboxInProduction", "true"));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void The_fake_gateway_counts_as_sandbox_even_in_live_mode_naming()
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Fake:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Fake:Mode", "Live"));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, "Production");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Live_gateway_with_a_live_key_and_disabled_gateways_pass_in_production()
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Stripe:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Stripe:Mode", "Live"),
            ("Subscriptions:PaymentGateways:Stripe:ApiKey", "sk_live_abc"),
            ("Subscriptions:PaymentGateways:Fake:Enabled", "false"));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, "Production");

        act.Should().NotThrow();
    }
}
