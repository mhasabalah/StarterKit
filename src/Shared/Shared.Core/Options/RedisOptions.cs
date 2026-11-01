namespace Shared.Core.Options;

/// <summary>
/// Redis cache configuration options
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis host address (default: localhost)
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Redis port (default: 6379)
    /// </summary>
    public int Port { get; set; } = 6379;

    /// <summary>
    /// Redis password (optional)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Path to Docker secret file containing password
    /// </summary>
    public string? PasswordSecretPath { get; set; } = "/run/secrets/redis_password";

    /// <summary>
    /// Key prefix for all cache entries (default: "StarterKit:")
    /// </summary>
    public string InstanceName { get; set; } = "StarterKit:";

    /// <summary>
    /// Whether to abort connection on failure (default: false)
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// Connection timeout in milliseconds (default: 5000)
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// Sync timeout in milliseconds (default: 5000)
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>
    /// Whether Redis is enabled (default: true)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Use SSL/TLS connection (default: false)
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Database number to use (default: 0)
    /// </summary>
    public int Database { get; set; } = 0;

    /// <summary>
    /// Build the Redis connection string
    ///
    /// Configuration Flow (Single Source of Truth):
    /// 1. Host/Port: From appsettings.json, overridden by env vars (REDIS_HOST, REDIS_PORT) in Docker
    /// 2. Password: From GetPassword() - the ONLY place handling password logic
    /// 3. Other settings: From appsettings.json properties
    /// </summary>
    public string GetConnectionString()
    {
        // Get password from the SINGLE source of truth
        // This handles production secrets vs development env vars
        var password = GetPassword();

        // Build connection string starting with host:port
        // Host and Port may have been overridden by RedisExtensions.OverrideFromEnvironment()
        var connectionString = $"{Host}:{Port}";

        // Add password if available
        if (!string.IsNullOrEmpty(password))
        {
            connectionString += $",password={password}";
        }

        // Add connection settings
        connectionString += $",abortConnect={AbortOnConnectFail.ToString().ToLower()}";
        connectionString += $",connectTimeout={ConnectTimeout}";
        connectionString += $",syncTimeout={SyncTimeout}";

        // Add compatibility options for Windows localhost connections
        connectionString += ",connectRetry=3";           // Retry connection 3 times
        connectionString += ",keepAlive=60";             // Keep connection alive (60 seconds)
        connectionString += ",allowAdmin=true";          // Allow admin commands (better error messages)
        connectionString += ",asyncTimeout=5000";        // Async operation timeout

        // Add SSL if enabled
        if (UseSsl)
        {
            connectionString += ",ssl=true";
        }

        // Add database number if not default (0)
        if (Database > 0)
        {
            connectionString += $",defaultDatabase={Database}";
        }

        return connectionString;
    }

    /// <summary>
    /// ⭐ SINGLE SOURCE OF TRUTH for Redis password ⭐
    ///
    /// This is the ONLY place that determines how to get the Redis password.
    /// Environment variables are NOT overridden in RedisExtensions.OverrideFromEnvironment()
    /// to keep password logic centralized here.
    ///
    /// PRODUCTION (ASPNETCORE_ENVIRONMENT = "Production" or NULL):
    ///   - Uses Docker secrets (file at /run/secrets/redis_password)
    ///   - Docker secrets are MOUNTED AS FILES, not environment variables
    ///   - REQUIRED - crashes if missing (fail fast for security)
    ///
    /// DEVELOPMENT (ASPNETCORE_ENVIRONMENT = "Development" or "Docker"):
    ///   - Uses REDIS_PASSWORD environment variable (from .env file)
    ///   - Falls back to Password property (from appsettings.json)
    ///   - Optional - can be null for local Redis without password
    /// </summary>
    private string? GetPassword()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var isProduction = string.IsNullOrEmpty(environment) ||
                          environment.Equals("Production", StringComparison.OrdinalIgnoreCase);

        if (isProduction)
        {
            // ======================================================
            // PRODUCTION: ONLY use Docker secret file
            // ======================================================
            // Why file? Docker secrets are mounted as FILES at /run/secrets/
            // They are NOT available as environment variables!
            // More secure: not visible in docker inspect or process lists

            return ReadSecretFileOrThrow();
        }
        else
        {
            // ======================================================
            // DEVELOPMENT: Use .env (env var) with config fallback
            // ======================================================
            // .env files set REDIS_PASSWORD as environment variable
            // Fallback to appsettings.json Password property

            return Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? Password;
        }
    }

    /// <summary>
    /// Read password from Docker secret file (production only)
    /// Throws exception if file is missing, unreadable, or empty
    /// </summary>
    private string ReadSecretFileOrThrow()
    {
        // Check if secret file path is configured
        if (string.IsNullOrEmpty(PasswordSecretPath))
        {
            throw new InvalidOperationException(
                "Production environment requires PasswordSecretPath to be configured. " +
                "Set Redis:PasswordSecretPath in appsettings.json (default: /run/secrets/redis_password)");
        }

        // Check if secret file exists
        if (!File.Exists(PasswordSecretPath))
        {
            throw new InvalidOperationException(
                $"Production environment REQUIRES Docker secret file at: {PasswordSecretPath}\n" +
                "Ensure your docker-compose.yml mounts the secret:\n" +
                "  secrets:\n" +
                "    - redis_password");
        }

        // Read secret file
        try
        {
            var secret = File.ReadAllText(PasswordSecretPath).Trim();

            if (string.IsNullOrEmpty(secret))
            {
                throw new InvalidOperationException(
                    $"Docker secret file exists but is EMPTY: {PasswordSecretPath}");
            }

            return secret;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to read Docker secret file: {PasswordSecretPath}\n" +
                $"Check file permissions and container access.", ex);
        }
    }

    /// <summary>
    /// Validate configuration
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Host) && Port > 0 && Port < 65536;
    }
}
