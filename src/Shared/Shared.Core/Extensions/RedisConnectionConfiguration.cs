using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Core.Options;
using StackExchange.Redis;

namespace Shared.Core.Extensions;

/// <summary>
/// Single source of truth for the Redis client configuration.
/// <list type="number">
/// <item><c>ConnectionStrings:redis</c> wins when set (Aspire <c>WithReference(redis)</c>, or the Docker secrets
/// provider). It carries endpoint, password and TLS.</item>
/// <item>Otherwise <see cref="RedisOptions.Host"/>/<see cref="RedisOptions.Port"/> (overridable by
/// <c>REDIS_HOST</c>/<c>REDIS_PORT</c> outside Development) and the password rules below.</item>
/// </list>
/// Client behaviour (timeouts, abort-on-connect) always comes from the <c>Redis</c> section.
/// <para>
/// Password (only without a connection string): Production requires the Docker secret file
/// (<see cref="RedisOptions.PasswordSecretPath"/>, fail fast); other environments use <c>REDIS_PASSWORD</c>
/// and then <see cref="RedisOptions.Password"/>.
/// </para>
/// </summary>
public static class RedisConnectionConfiguration
{
    private const int ConnectRetryCount = 3;
    private const int KeepAliveSeconds = 60;

    public static ConfigurationOptions Resolve(IConfiguration configuration, RedisOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = configuration.GetConnectionString(RedisOptions.ConnectionStringName);
        var redis = string.IsNullOrWhiteSpace(connectionString)
            ? FromOptions(configuration, options)
            : ConfigurationOptions.Parse(connectionString);

        redis.AbortOnConnectFail = options.AbortOnConnectFail;
        redis.ConnectTimeout = options.ConnectTimeout;
        redis.SyncTimeout = options.SyncTimeout;
        redis.AsyncTimeout = options.SyncTimeout;
        redis.ConnectRetry = ConnectRetryCount;
        redis.KeepAlive = KeepAliveSeconds;

        return redis;
    }

    private static ConfigurationOptions FromOptions(IConfiguration configuration, RedisOptions options)
    {
        var environment = EnvironmentName(configuration);
        var host = options.Host;
        var port = options.Port;

        // Container platforms (compose/k8s) inject the service address; a developer machine uses appsettings.
        if (!IsEnvironment(environment, Environments.Development))
        {
            if (configuration["REDIS_HOST"] is { Length: > 0 } envHost)
                host = envHost;
            if (int.TryParse(configuration["REDIS_PORT"], out var envPort))
                port = envPort;
        }

        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or >= 65536)
        {
            throw new InvalidOperationException(
                $"Invalid Redis configuration: set ConnectionStrings:{RedisOptions.ConnectionStringName} " +
                $"or a valid {RedisOptions.SectionName}:Host and {RedisOptions.SectionName}:Port (got '{host}:{port}').");
        }

        var redis = new ConfigurationOptions
        {
            EndPoints = { { host, port } },
            Password = ResolvePassword(configuration, options, environment),
            Ssl = options.UseSsl,
        };

        if (options.Database > 0)
            redis.DefaultDatabase = options.Database;

        return redis;
    }

    private static string? ResolvePassword(IConfiguration configuration, RedisOptions options, string environment)
    {
        if (IsEnvironment(environment, Environments.Production))
            return ReadSecretFileOrThrow(options.PasswordSecretPath);

        var fromEnvironment = configuration["REDIS_PASSWORD"];
        var password = string.IsNullOrEmpty(fromEnvironment) ? options.Password : fromEnvironment;
        return string.IsNullOrEmpty(password) ? null : password;
    }

    private static string ReadSecretFileOrThrow(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Production requires {RedisOptions.SectionName}:PasswordSecretPath (default /run/secrets/redis_password) " +
                $"or ConnectionStrings:{RedisOptions.ConnectionStringName}.");
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Production requires the Redis password secret file at {path} " +
                "(mount the 'redis_password' Docker secret) or ConnectionStrings:" + RedisOptions.ConnectionStringName + ".");
        }

        string secret;
        try
        {
            secret = File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to read the Redis password secret file {path}.", ex);
        }

        return secret.Length > 0
            ? secret
            : throw new InvalidOperationException($"The Redis password secret file {path} is empty.");
    }

    // Same precedence as the generic host: ASPNETCORE_ENVIRONMENT, then DOTNET_ENVIRONMENT, else Production.
    private static string EnvironmentName(IConfiguration configuration) =>
        configuration["ASPNETCORE_ENVIRONMENT"] is { Length: > 0 } aspNet ? aspNet
        : configuration["DOTNET_ENVIRONMENT"] is { Length: > 0 } dotNet ? dotNet
        : Environments.Production;

    private static bool IsEnvironment(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
