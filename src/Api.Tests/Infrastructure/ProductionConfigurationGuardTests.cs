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
            ("Hangfire:DashboardAuth:Password", "change_me"));

        var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, "Production");

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("ConnectionStrings:starterkit").And.Contain("Hangfire:DashboardAuth:Password");
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

    [Fact]
    public void The_Docker_settings_do_not_block_startup_on_the_RabbitMq_password_the_default_transport_never_uses()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Docker.json"))
            .Build();

        ProductionConfigurationGuard.FindPlaceholders(config).Should().NotContain("Messaging:RabbitMq:Password");
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
    [InlineData("Development", "InMemory")]
    [InlineData("Development", "RabbitMq")]
    [InlineData("Testing", null)]
    public void Allows_durable_transport_or_relaxed_environment(string environment, string? transport)
    {
        var config = Config(("Messaging:Transport", transport));

        var act = () => ProductionConfigurationGuard.EnsureDurableMessaging(config, environment);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("rabbitmq", "secret", null)]
    [InlineData("127.0.0.1", "secret", null)]
    [InlineData("localhost", null, "amqp://user:pass@broker:5672/")]
    [InlineData(null, null, "amqp://user:pass@broker:5672/")]
    public void Allows_RabbitMq_with_a_broker_address_and_password(string? host, string? password, string? connectionString)
    {
        var config = Config(
            ("Messaging:Transport", "RabbitMq"),
            ("Messaging:RabbitMq:Host", host),
            ("Messaging:RabbitMq:Password", password),
            ("ConnectionStrings:rabbitmq", connectionString));

        var act = () => ProductionConfigurationGuard.EnsureDurableMessaging(config, "Production");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    public void Refuses_RabbitMq_outside_dev_without_a_broker_address(string? host)
    {
        var config = Config(
            ("Messaging:Transport", "RabbitMq"),
            ("Messaging:RabbitMq:Host", host),
            ("Messaging:RabbitMq:Password", "secret"));

        var act = () => ProductionConfigurationGuard.EnsureDurableMessaging(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Messaging:RabbitMq:Host*");
    }

    [Fact]
    public void Refuses_RabbitMq_in_Docker_without_the_overlay_that_supplies_the_password()
    {
        // MESSAGING_TRANSPORT=RabbitMq without docker-compose.rabbitmq.yml: the Docker settings name the host, nothing
        // provides the password.
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Docker.json"))
            .AddInMemoryCollection([new("Messaging:Transport", "RabbitMq")])
            .Build();

        var act = () => ProductionConfigurationGuard.EnsureDurableMessaging(config, "Docker");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Messaging:RabbitMq:Password*");
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

    [Theory]
    [InlineData("Live", "egy_sk_test_abc", null)]
    [InlineData("Live", null, "egy_pk_test_abc")]
    [InlineData("Sandbox", "ksa_sk_live_abc", null)]
    [InlineData("Sandbox", null, "uae_pk_live_abc")]
    [InlineData("Live", "rk_test_abc", null)]
    public void Paymob_and_restricted_stripe_keys_are_recognized_by_their_shape(string mode, string? apiKey, string? publicKey)
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Paymob:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Paymob:Mode", mode),
            ("Subscriptions:PaymentGateways:Paymob:ApiKey", apiKey),
            ("Subscriptions:PaymentGateways:Paymob:PublicKey", publicKey));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, "Development");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Paymob*");
    }

    [Fact]
    public void A_test_secret_with_a_live_public_key_is_refused_as_mixed()
    {
        var config = Config(
            ("Subscriptions:PaymentGateways:Paymob:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Paymob:Mode", "Live"),
            ("Subscriptions:PaymentGateways:Paymob:ApiKey", "egy_sk_test_abc"),
            ("Subscriptions:PaymentGateways:Paymob:PublicKey", "egy_pk_live_abc"));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(config, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*mixes*");
    }

    [Fact]
    public void Matching_paymob_live_keys_pass_and_a_lookalike_word_is_not_taken_for_a_key()
    {
        var live = Config(
            ("Subscriptions:PaymentGateways:Paymob:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Paymob:Mode", "Live"),
            ("Subscriptions:PaymentGateways:Paymob:ApiKey", "egy_sk_live_abc"),
            ("Subscriptions:PaymentGateways:Paymob:PublicKey", "egy_pk_live_abc"));
        var lookalike = Config(
            ("Subscriptions:PaymentGateways:Stripe:Enabled", "true"),
            ("Subscriptions:PaymentGateways:Stripe:Mode", "Live"),
            ("Subscriptions:PaymentGateways:Stripe:ApiKey", "desk_test_notakey"));

        var act = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(live, "Production");
        var other = () => ProductionConfigurationGuard.EnsurePaymentGatewayModes(lookalike, "Production");

        act.Should().NotThrow();
        other.Should().NotThrow();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Docker")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void LoadTest_profile_outside_the_LoadTest_environment_blocks_startup(string environment)
    {
        var config = Config(("LoadTest:Enabled", "true"));

        var act = () => ProductionConfigurationGuard.EnsureLoadTestProfileIsIsolated(config, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*LoadTest:Enabled*");
    }

    [Theory]
    [InlineData("LoadTest", "true")]
    [InlineData("loadtest", "true")]
    [InlineData("Production", "false")]
    [InlineData("Production", null)]
    public void LoadTest_profile_is_allowed_in_its_own_environment_or_when_not_enabled(string environment, string? enabled)
    {
        var config = Config(("LoadTest:Enabled", enabled));

        var act = () => ProductionConfigurationGuard.EnsureLoadTestProfileIsIsolated(config, environment);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("LoadTest", "false")]
    [InlineData("loadtest", null)]
    public void LoadTest_environment_without_the_profile_blocks_startup(string environment, string? enabled)
    {
        var config = Config(("LoadTest:Enabled", enabled));

        var act = () => ProductionConfigurationGuard.EnsureLoadTestProfileIsIsolated(config, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*profile*missing*");
    }

    /// <summary>The shipped settings exactly as the Docker image loads them (appsettings.json + appsettings.Docker.json).</summary>
    private static IConfigurationBuilder ShippedDockerSettings()
    {
        var api = Path.Combine(FindRepositoryRoot(), "src", "Api");
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(api, "appsettings.Docker.json"), optional: false);
    }

    [Fact]
    public void Shipped_settings_keep_placeholders_only_for_values_every_deployment_provides()
    {
        // A placeholder in an optional, disabled-by-default feature (APNs, payment gateways) made the guard refuse to
        // boot every production deployment that did not use the feature. Optional secrets default to "" instead; their
        // own validation rejects them as incomplete only when the feature is enabled.
        ProductionConfigurationGuard.FindPlaceholders(ShippedDockerSettings().Build()).Should().BeEquivalentTo(
            "ConnectionStrings:starterkit",    // built from the postgres_password Docker secret
            "Hangfire:DashboardAuth:Password", // HANGFIRE_DASHBOARD_PASSWORD in docker-compose.yml
            "JwtSettings:SecretKey");          // the jwt_secret Docker secret
    }

    [Fact]
    public void Production_boots_with_what_the_base_compose_file_and_its_secrets_provide()
    {
        var secrets = Directory.CreateTempSubdirectory("prod-secrets").FullName;
        try
        {
            File.WriteAllText(Path.Combine(secrets, "jwt_secret"), "a-real-jwt-secret-of-at-least-32-characters!");
            File.WriteAllText(Path.Combine(secrets, "postgres_password"), "a-real-database-password");

            var config = ShippedDockerSettings()
                // docker-compose.yml: Hangfire__DashboardAuth__Password: ${HANGFIRE_DASHBOARD_PASSWORD:-}
                .AddInMemoryCollection([new("Hangfire:DashboardAuth:Password", "")])
                .AddDockerSecrets(secrets, "Production")
                .Build();

            var act = () => ProductionConfigurationGuard.EnsureNoPlaceholders(config, "Production");

            act.Should().NotThrow("an optional feature that is switched off must not block a production boot");
        }
        finally
        {
            Directory.Delete(secrets, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (*.slnx) not found.");
    }
}
