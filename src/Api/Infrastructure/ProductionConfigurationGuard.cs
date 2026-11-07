namespace Api.Infrastructure;

/// <summary>
/// Fail-fast guard: the template ships appsettings with <c>CHANGE_ME</c> placeholders (DB password, JWT secret,
/// Hangfire dashboard password, S3 bucket, ...). Outside Development/Testing the app refuses to boot while any
/// placeholder is still in effect. Without this guard, production could quietly run with a guessable JWT key.
/// <para>
/// Values can be overridden by environment variables, Docker secrets (<c>/run/secrets</c>) or any other
/// configuration provider. Only the final, merged value is checked.
/// </para>
/// </summary>
public static class ProductionConfigurationGuard
{
    public const string Placeholder = "CHANGE_ME";

    /// <summary>Environments where placeholders are tolerated (local dev, tests, CI).</summary>
    private static readonly HashSet<string> RelaxedEnvironments =
        new(StringComparer.OrdinalIgnoreCase) { "Development", "Testing", "Test", "E2E" };

    /// <summary>Returns the configuration KEYS (never the values) that still contain the placeholder.</summary>
    public static IReadOnlyList<string> FindPlaceholders(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.AsEnumerable()
            .Where(kv => kv.Value is not null &&
                         kv.Value.Contains(Placeholder, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Throws if the environment is not relaxed and any placeholder remains.</summary>
    public static void EnsureNoPlaceholders(IConfiguration configuration, string environmentName)
    {
        if (RelaxedEnvironments.Contains(environmentName))
        {
            return;
        }

        var offending = FindPlaceholders(configuration);
        if (offending.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start in '{environmentName}': {offending.Count} configuration value(s) still contain " +
            $"the '{Placeholder}' placeholder. Set them via environment variables or Docker secrets: " +
            string.Join(", ", offending));
    }

    /// <summary>
    /// Throws if the environment is not relaxed and <c>Messaging:Transport</c> is <c>InMemory</c> (explicit or the
    /// default when missing): in-memory messaging is not durable, so integration events and outbox deliveries in
    /// flight would be lost on every restart or deploy.
    /// </summary>
    public static void EnsureDurableMessaging(IConfiguration configuration, string environmentName)
    {
        if (RelaxedEnvironments.Contains(environmentName))
        {
            return;
        }

        var transport = configuration["Messaging:Transport"];
        if (string.IsNullOrWhiteSpace(transport) ||
            string.Equals(transport, nameof(Shared.Messaging.Extensions.MessagingTransport.InMemory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to start in '{environmentName}': Messaging:Transport is InMemory (not durable). " +
                "Set Messaging:Transport to PostgreSql or RabbitMq (env MESSAGING_TRANSPORT).");
        }
    }

    /// <summary>
    /// Throws if the environment is not relaxed and <c>BotProtection:Enabled=true</c> without a real
    /// <c>BotProtection:SecretKey</c> (empty or still the placeholder): CAPTCHA would silently reject every request.
    /// </summary>
    public static void EnsureBotProtectionConfigured(IConfiguration configuration, string environmentName)
    {
        if (RelaxedEnvironments.Contains(environmentName))
        {
            return;
        }

        var enabled = configuration.GetValue<bool>("BotProtection:Enabled");
        var secret = configuration["BotProtection:SecretKey"];
        if (enabled && (string.IsNullOrWhiteSpace(secret) ||
                        secret.Contains(Placeholder, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Refusing to start in '{environmentName}': BotProtection:Enabled is true but BotProtection:SecretKey " +
                "is empty or a placeholder. Set it via env BotProtection__SecretKey or a Docker secret.");
        }
    }

    /// <summary>The environment that may run the load-test profile (<c>appsettings.LoadTest.json</c>).</summary>
    public const string LoadTestEnvironment = "LoadTest";

    /// <summary>
    /// Keeps the load-test profile (<c>appsettings.LoadTest.json</c>: relaxed rate limits and 2FA) apart from real
    /// deployments, in both directions:
    /// <list type="bullet">
    /// <item><c>LoadTest:Enabled=true</c> in any environment other than <c>LoadTest</c> throws (Development and
    /// Production included), so a copied file or variable cannot leak the profile.</item>
    /// <item>The <c>LoadTest</c> environment without <c>LoadTest:Enabled=true</c> throws: the profile file is not part of
    /// published images, so a "LoadTest" host without it is a mislabelled deployment running on the base settings.</item>
    /// </list>
    /// </summary>
    public static void EnsureLoadTestProfileIsIsolated(IConfiguration configuration, string environmentName)
    {
        var enabled = configuration.GetValue<bool>("LoadTest:Enabled");
        var isLoadTestEnvironment = string.Equals(environmentName, LoadTestEnvironment, StringComparison.OrdinalIgnoreCase);

        if (enabled && !isLoadTestEnvironment)
        {
            throw new InvalidOperationException(
                $"Refusing to start in '{environmentName}': LoadTest:Enabled is true (load-test profile: relaxed rate " +
                $"limits and 2FA). Only the '{LoadTestEnvironment}' environment may run it; remove the LoadTest section.");
        }

        if (!enabled && isLoadTestEnvironment)
        {
            throw new InvalidOperationException(
                $"Refusing to start in '{environmentName}': LoadTest:Enabled is not true. The load-test profile " +
                "(appsettings.LoadTest.json, mounted by docker-compose.loadtest.yml) is missing.");
        }
    }

    public static WebApplicationBuilder EnsureProductionReadyConfiguration(this WebApplicationBuilder builder)
    {
        EnsureNoPlaceholders(builder.Configuration, builder.Environment.EnvironmentName);
        EnsureDurableMessaging(builder.Configuration, builder.Environment.EnvironmentName);
        EnsureBotProtectionConfigured(builder.Configuration, builder.Environment.EnvironmentName);
        EnsureLoadTestProfileIsIsolated(builder.Configuration, builder.Environment.EnvironmentName);
        return builder;
    }
}
