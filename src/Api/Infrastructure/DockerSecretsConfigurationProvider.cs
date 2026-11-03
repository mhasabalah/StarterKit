using Microsoft.Extensions.Configuration;

namespace Api.Infrastructure;

/// <summary>
/// Reads Docker secrets (files under <c>/run/secrets</c>) into configuration.
/// <list type="number">
/// <item><b>Generic (every file).</b> File name → config key, with <c>__</c> as the section separator
/// (same convention as KeyPerFile / environment variables). Example: <c>Email__SmtpPassword</c> →
/// <c>Email:SmtpPassword</c>. The raw file name is also exposed as a key. Adding a secret therefore needs
/// no code change.</item>
/// <item><b>Well-known mappings</b> for legacy secret names, see <see cref="KnownMappings"/>. They also build the
/// Postgres and Redis connection strings from their passwords.</item>
/// </list>
/// Secret VALUES are never written to the console or logs; only key names are.
/// Skipped in Development (docker-compose.override.yml uses plain environment variables).
/// </summary>
public class DockerSecretsConfigurationProvider(string secretsPath, string? environmentName) : ConfigurationProvider
{
    public const string DefaultSecretsPath = "/run/secrets";

    /// <summary>Legacy or short secret file names → configuration keys.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> KnownMappings =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["jwt_secret"] = ["JWT_SECRET", "JwtSettings:SecretKey"],
            ["postgres_password"] = ["POSTGRES_PASSWORD"],
            ["redis_password"] = ["REDIS_PASSWORD"],
            ["identity_otp_hmac_key"] = ["Identity:OtpHmacKey"],
            ["hangfire_dashboard_password"] = ["Hangfire:Dashboard:Password"],
            ["smtp_password"] = ["Email:SmtpPassword"],
            ["rabbitmq_password"] = ["Messaging:RabbitMq:Password"],
            // Data Protection key-ring encryption (docker-compose.dataprotection.yml). The PFX itself is mounted as
            // /run/secrets/dataprotection_cert.pfx and referenced by Identity:DataProtection:Certificate:Path.
            ["dataprotection_cert_password"] = ["Identity:DataProtection:Certificate:Password"],
        };

    /// <summary>Secret files that are binary (e.g. a PFX) and must not be loaded as configuration values.</summary>
    public static readonly IReadOnlySet<string> BinarySecretExtensions =
        new HashSet<string>([".pfx", ".p12"], StringComparer.OrdinalIgnoreCase);

    public override void Load()
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[secrets] Development environment - Docker secrets skipped.");
            return;
        }

        if (!Directory.Exists(secretsPath))
        {
            Console.WriteLine($"[secrets] {secretsPath} not found - using environment variables / appsettings.");
            return;
        }

        var loaded = new List<string>();
        foreach (var file in Directory.EnumerateFiles(secretsPath).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
            {
                continue; // ignore hidden files (e.g. Kubernetes ..data symlinks)
            }

            if (BinarySecretExtensions.Contains(Path.GetExtension(name)))
            {
                continue; // binary secrets (certificates) are read by path, never as configuration text
            }

            var value = ReadSecret(file);
            if (value is null)
            {
                continue;
            }

            Data[name] = value;
            Data[name.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal)] = value;

            if (KnownMappings.TryGetValue(name, out var keys))
            {
                foreach (var key in keys)
                {
                    Data[key] = value;
                }
            }

            loaded.Add(name);
        }

        BuildConnectionStrings();

        Console.WriteLine(loaded.Count == 0
            ? $"[secrets] No secrets found in {secretsPath}."
            : $"[secrets] Loaded {loaded.Count} secret(s): {string.Join(", ", loaded)}");

        if (Data.TryGetValue("JwtSettings:SecretKey", out var jwt) && jwt is { Length: < 32 })
        {
            Console.WriteLine("[secrets] WARNING: jwt_secret is shorter than 32 characters.");
        }
    }

    private void BuildConnectionStrings()
    {
        if (Data.TryGetValue("POSTGRES_PASSWORD", out var pg) && !string.IsNullOrEmpty(pg))
        {
            var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "postgres";
            var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
            var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "starterkit";
            var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
            Data["ConnectionStrings:starterkit"] =
                $"Host={host};Port={port};Database={database};Username={username};Password={pg};Include Error Detail=false";
        }

        if (Data.TryGetValue("REDIS_PASSWORD", out var redis) && !string.IsNullOrEmpty(redis))
        {
            var host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "redis";
            var port = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
            Data["ConnectionStrings:Redis"] = $"{host}:{port},password={redis},ssl=false,abortConnect=false";
        }
    }

    private static string? ReadSecret(string path)
    {
        try
        {
            var secret = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(secret) ? null : secret;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[secrets] ERROR reading {Path.GetFileName(path)}: {ex.GetType().Name}");
            return null;
        }
    }
}

/// <summary>Configuration source for Docker secrets.</summary>
public class DockerSecretsConfigurationSource(string secretsPath, string? environmentName) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new DockerSecretsConfigurationProvider(secretsPath, environmentName);
}

public static class DockerSecretsConfigurationExtensions
{
    /// <summary>Adds Docker secrets from <paramref name="secretsPath"/> (default <c>/run/secrets</c>).</summary>
    public static IConfigurationBuilder AddDockerSecrets(
        this IConfigurationBuilder builder,
        string secretsPath = DockerSecretsConfigurationProvider.DefaultSecretsPath,
        string? environmentName = null)
    {
        environmentName ??= Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        builder.Add(new DockerSecretsConfigurationSource(secretsPath, environmentName));
        return builder;
    }
}
