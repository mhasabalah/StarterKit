namespace Shared.Core.Options;

/// <summary>
/// Redis cache configuration (section <c>Redis</c>).
/// <para>
/// WHERE Redis is (endpoint, password, TLS) comes from <c>ConnectionStrings:redis</c> when present - injected by
/// .NET Aspire (<c>WithReference(redis)</c>) or built from the <c>redis_password</c> Docker secret - and otherwise
/// from <see cref="Host"/>/<see cref="Port"/>/<see cref="Password"/>. HOW the client behaves (timeouts,
/// abort-on-connect) always comes from this section. Resolution lives in
/// <see cref="Shared.Core.Extensions.RedisConnectionConfiguration"/>.
/// </para>
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Connection string name (<c>ConnectionStrings:redis</c>); matches the Aspire resource name.</summary>
    public const string ConnectionStringName = "redis";

    /// <summary>Redis host when no connection string is configured (default: localhost).</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Redis port when no connection string is configured (default: 6379).</summary>
    public int Port { get; set; } = 6379;

    /// <summary>Redis password for non-production environments (production reads <see cref="PasswordSecretPath"/>).</summary>
    public string? Password { get; set; }

    /// <summary>Docker secret file holding the production password.</summary>
    public string? PasswordSecretPath { get; set; } = "/run/secrets/redis_password";

    /// <summary>Key prefix for all cache entries (default: "StarterKit:").</summary>
    public string InstanceName { get; set; } = "StarterKit:";

    /// <summary>
    /// Whether the first connect failure aborts (default: false). Keep false: the multiplexer then retries in the
    /// background, so a Redis restart degrades the cache instead of failing application startup.
    /// </summary>
    public bool AbortOnConnectFail { get; set; }

    /// <summary>Connection timeout in milliseconds (default: 5000).</summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>Sync and async operation timeout in milliseconds (default: 5000).</summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>Whether Redis is enabled (default: true). False = in-process memory cache (single instance only).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Use TLS when no connection string is configured (default: false).</summary>
    public bool UseSsl { get; set; }

    /// <summary>Database number when no connection string is configured (default: 0).</summary>
    public int Database { get; set; }

    /// <summary>True when <see cref="Host"/> and <see cref="Port"/> describe a usable endpoint.</summary>
    public bool IsValid() => !string.IsNullOrWhiteSpace(Host) && Port is > 0 and < 65536;
}
