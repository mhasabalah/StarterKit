using Microsoft.Extensions.Configuration;

namespace Api.Infrastructure;

/// <summary>
/// Configuration provider that reads Docker secrets from /run/secrets/
/// and makes them available as configuration values.
/// </summary>
public class DockerSecretsConfigurationProvider : ConfigurationProvider
{
    private const string SecretsPath = "/run/secrets";
    private readonly ILogger<DockerSecretsConfigurationProvider>? _logger;

    public DockerSecretsConfigurationProvider(ILogger<DockerSecretsConfigurationProvider>? logger = null)
    {
        _logger = logger;
    }

    public override void Load()
    {
        // Console output (always visible in Docker logs)
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine("  Docker Secrets Configuration Provider");
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Also log to Serilog if available
        _logger?.LogInformation("════════════════════════════════════════════════════════");
        _logger?.LogInformation("  Docker Secrets Configuration Provider");
        _logger?.LogInformation("════════════════════════════════════════════════════════");
        _logger?.LogInformation("");

        // Check environment - only use secrets in Docker/Production, not Development
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (environment == "Development")
        {
            Console.WriteLine($"ℹ️  Running in Development environment - skipping Docker secrets");
            Console.WriteLine("ℹ️  Using environment variables from docker-compose.override.yml");
            Console.WriteLine("════════════════════════════════════════════════════════");
            Console.WriteLine();

            _logger?.LogInformation("ℹ️  Running in Development environment - skipping Docker secrets");
            return;
        }

        // Check if running in Docker with secrets
        if (!Directory.Exists(SecretsPath))
        {
            Console.WriteLine($"ℹ️  {SecretsPath} not found - not running with Docker secrets");
            Console.WriteLine("ℹ️  Using environment variables or appsettings.json instead");
            Console.WriteLine("════════════════════════════════════════════════════════");
            Console.WriteLine();

            _logger?.LogInformation("ℹ️  {SecretsPath} not found - not running with Docker secrets", SecretsPath);
            _logger?.LogInformation("ℹ️  Using environment variables or appsettings.json instead");
            return;
        }

        Console.WriteLine($"🔐 Reading Docker secrets from {SecretsPath}...");
        Console.WriteLine();

        _logger?.LogInformation("🔐 Reading Docker secrets from {SecretsPath}...", SecretsPath);
        _logger?.LogInformation("");

        var secretsLoaded = 0;

        // Read PostgreSQL password
        var postgresPassword = ReadSecret("postgres_password");
        if (!string.IsNullOrEmpty(postgresPassword))
        {
            secretsLoaded++;
            Data["POSTGRES_PASSWORD"] = postgresPassword;
            Console.WriteLine($"  ✅ Loaded postgres_password ({postgresPassword.Length} chars)");
            _logger?.LogInformation("  ✅ Loaded postgres_password ({Length} chars)", postgresPassword.Length);

            // Build connection string
            var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "postgres";
            var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
            var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "starterkit";
            var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";

            var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={postgresPassword};Include Error Detail=false";
            Data["ConnectionStrings:starterkit"] = connectionString;

            Console.WriteLine("  ✅ Built ConnectionStrings:starterkit");
            Console.WriteLine($"     Host: {host}:{port}");
            Console.WriteLine($"     Database: {database}");
            Console.WriteLine($"     Username: {username}");

            _logger?.LogInformation("  ✅ Built ConnectionStrings:starterkit");
            _logger?.LogInformation("     Host: {Host}:{Port}", host, port);
            _logger?.LogInformation("     Database: {Database}", database);
            _logger?.LogInformation("     Username: {Username}", username);
        }
        else
        {
            Console.WriteLine("  ⚠️  postgres_password not found");
            _logger?.LogWarning("  ⚠️  postgres_password not found");
        }

        Console.WriteLine();
        _logger?.LogInformation("");

        // Read Redis password
        var redisPassword = ReadSecret("redis_password");
        if (!string.IsNullOrEmpty(redisPassword))
        {
            secretsLoaded++;
            Data["REDIS_PASSWORD"] = redisPassword;
            Console.WriteLine($"  ✅ Loaded redis_password ({redisPassword.Length} chars)");
            _logger?.LogInformation("  ✅ Loaded redis_password ({Length} chars)", redisPassword.Length);

            // Build connection string
            var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "redis";
            var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";

            var redisConnectionString = $"{redisHost}:{redisPort},password={redisPassword},ssl=false,abortConnect=false";
            Data["ConnectionStrings:Redis"] = redisConnectionString;

            Console.WriteLine("  ✅ Built ConnectionStrings:Redis");
            Console.WriteLine($"     Host: {redisHost}:{redisPort}");

            _logger?.LogInformation("  ✅ Built ConnectionStrings:Redis");
            _logger?.LogInformation("     Host: {Host}:{Port}", redisHost, redisPort);
        }
        else
        {
            Console.WriteLine("  ⚠️  redis_password not found");
            _logger?.LogWarning("  ⚠️  redis_password not found");
        }

        Console.WriteLine();
        _logger?.LogInformation("");

        // Read JWT secret
        var jwtSecret = ReadSecret("jwt_secret");
        if (!string.IsNullOrEmpty(jwtSecret))
        {
            secretsLoaded++;
            Data["JWT_SECRET"] = jwtSecret;
            Data["JwtSettings:SecretKey"] = jwtSecret;
            Console.WriteLine($"  ✅ Loaded jwt_secret ({jwtSecret.Length} chars)");
            Console.WriteLine("  ✅ Set JwtSettings:SecretKey");

            _logger?.LogInformation("  ✅ Loaded jwt_secret ({Length} chars)", jwtSecret.Length);
            _logger?.LogInformation("  ✅ Set JwtSettings:SecretKey");

            // Validate secret strength
            if (jwtSecret.Length < 32)
            {
                Console.WriteLine("  ⚠️  WARNING: JWT secret is weak (< 32 chars)");
                _logger?.LogWarning("  ⚠️  WARNING: JWT secret is weak (< 32 chars)");
            }
        }
        else
        {
            Console.WriteLine("  ⚠️  jwt_secret not found");
            _logger?.LogWarning("  ⚠️  jwt_secret not found");
        }

        Console.WriteLine();
        Console.WriteLine("════════════════════════════════════════════════════════");
        if (secretsLoaded > 0)
        {
            Console.WriteLine($"  ✅ Docker Secrets Loaded Successfully ({secretsLoaded}/3)");
            _logger?.LogInformation("  ✅ Docker Secrets Loaded Successfully ({Count}/3)", secretsLoaded);
        }
        else
        {
            Console.WriteLine("  ℹ️  No Docker secrets found");
            Console.WriteLine("  ℹ️  Using environment variables or appsettings.json instead");
            _logger?.LogInformation("  ℹ️  No Docker secrets found");
            _logger?.LogInformation("  ℹ️  Using environment variables or appsettings.json instead");
        }
        Console.WriteLine("════════════════════════════════════════════════════════");
        Console.WriteLine();

        _logger?.LogInformation("");
        _logger?.LogInformation("════════════════════════════════════════════════════════");
        _logger?.LogInformation("");
    }

    private string? ReadSecret(string secretName)
    {
        var secretPath = Path.Combine(SecretsPath, secretName);

        if (!File.Exists(secretPath))
        {
            return null;
        }

        try
        {
            // Read secret and trim whitespace/newlines
            var secret = File.ReadAllText(secretPath).Trim();
            return string.IsNullOrWhiteSpace(secret) ? null : secret;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error reading secret: {secretName} - {ex.Message}");
            _logger?.LogError(ex, "❌ Error reading secret: {SecretName}", secretName);
            return null;
        }
    }
}

/// <summary>
/// Configuration source for Docker secrets.
/// </summary>
public class DockerSecretsConfigurationSource : IConfigurationSource
{
    private readonly ILogger<DockerSecretsConfigurationProvider>? _logger;

    public DockerSecretsConfigurationSource(ILogger<DockerSecretsConfigurationProvider>? logger = null)
    {
        _logger = logger;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DockerSecretsConfigurationProvider(_logger);
    }
}

/// <summary>
/// Extension methods for adding Docker secrets configuration.
/// </summary>
public static class DockerSecretsConfigurationExtensions
{
    /// <summary>
    /// Adds Docker secrets from /run/secrets/ to configuration.
    /// </summary>
    public static IConfigurationBuilder AddDockerSecrets(
        this IConfigurationBuilder builder,
        ILogger<DockerSecretsConfigurationProvider>? logger = null)
    {
        builder.Add(new DockerSecretsConfigurationSource(logger));
        return builder;
    }
}
