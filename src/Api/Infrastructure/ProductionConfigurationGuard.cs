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

    /// <summary>
    /// Payment gateway mode guard (<c>Subscriptions:PaymentGateways:&lt;Name&gt;:Mode</c> = Sandbox | Live, default Sandbox):
    /// <list type="bullet">
    /// <item>in EVERY environment an enabled gateway whose key contradicts its mode is refused (<c>sk_test_</c>/<c>rk_test_</c>
    /// in Live, <c>sk_live_</c>/<c>rk_live_</c> in Sandbox), so test keys never take real orders and live keys never run
    /// against a half-configured sandbox;</item>
    /// <item>in Production an enabled Sandbox gateway (the Fake adapter counts as one) is refused unless
    /// <c>AllowSandboxInProduction=true</c>.</item>
    /// </list>
    /// </summary>
    public static void EnsurePaymentGatewayModes(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var production = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
        foreach (var gateway in configuration.GetSection("Subscriptions:PaymentGateways").GetChildren())
        {
            if (!gateway.GetValue<bool>("Enabled"))
            {
                continue;
            }

            var isLive = string.Equals(gateway["Mode"], "Live", StringComparison.OrdinalIgnoreCase);
            var key = gateway["ApiKey"] ?? string.Empty;
            var testKey = key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("rk_test_", StringComparison.Ordinal);
            var liveKey = key.StartsWith("sk_live_", StringComparison.Ordinal) || key.StartsWith("rk_live_", StringComparison.Ordinal);

            if (isLive && testKey)
            {
                throw new InvalidOperationException(
                    $"Refusing to start: payment gateway '{gateway.Key}' is Mode=Live but its ApiKey is a test key.");
            }

            if (!isLive && liveKey)
            {
                throw new InvalidOperationException(
                    $"Refusing to start: payment gateway '{gateway.Key}' is Mode=Sandbox (the default) but its ApiKey is a live key. " +
                    "Set Subscriptions:PaymentGateways:" + gateway.Key + ":Mode=Live.");
            }

            var sandbox = !isLive || string.Equals(gateway.Key, "Fake", StringComparison.OrdinalIgnoreCase);
            if (production && sandbox && !gateway.GetValue<bool>("AllowSandboxInProduction"))
            {
                throw new InvalidOperationException(
                    $"Refusing to start in Production: payment gateway '{gateway.Key}' is a sandbox gateway. " +
                    "Use live keys with Mode=Live, or set AllowSandboxInProduction=true for a demo stack.");
            }
        }
    }

    public static WebApplicationBuilder EnsureProductionReadyConfiguration(this WebApplicationBuilder builder)
    {
        EnsureNoPlaceholders(builder.Configuration, builder.Environment.EnvironmentName);
        EnsureDurableMessaging(builder.Configuration, builder.Environment.EnvironmentName);
        EnsureBotProtectionConfigured(builder.Configuration, builder.Environment.EnvironmentName);
        EnsurePaymentGatewayModes(builder.Configuration, builder.Environment.EnvironmentName);
        return builder;
    }
}
