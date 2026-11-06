using Microsoft.Extensions.Configuration;

namespace AppHost;

/// <summary>
/// AppHost switches. Set them in <c>appsettings.json</c>, user secrets, environment variables
/// (<c>Observability__FullStack=true</c>) or the command line (<c>dotnet run -- Observability:FullStack=true</c>).
/// </summary>
public sealed record AppHostSettings(
    bool ObservabilityFullStack,
    MessagingTransport Transport,
    int? PostgresHostPort,
    bool ClamAv = false)
{
    public const string ObservabilityFullStackKey = "Observability:FullStack";
    public const string TransportKey = "Messaging:Transport";
    public const string PostgresHostPortKey = "Postgres:HostPort";
    public const string ClamAvKey = "Attachment:ClamAv";

    public static AppHostSettings From(IConfiguration configuration)
    {
        var transportValue = configuration[TransportKey];
        var transport = MessagingTransport.InMemory;
        if (!string.IsNullOrWhiteSpace(transportValue) &&
            (!Enum.TryParse(transportValue, ignoreCase: true, out transport) || !Enum.IsDefined(transport)))
        {
            throw new InvalidOperationException(
                $"{TransportKey} must be one of {string.Join(", ", Enum.GetNames<MessagingTransport>())} (got '{transportValue}').");
        }

        int? postgresPort = null;
        if (configuration[PostgresHostPortKey] is { Length: > 0 } portValue)
        {
            postgresPort = int.TryParse(portValue, out var port) && port is > 0 and < 65536
                ? port
                : throw new InvalidOperationException($"{PostgresHostPortKey} must be a TCP port (got '{portValue}').");
        }

        return new AppHostSettings(
            configuration.GetValue(ObservabilityFullStackKey, defaultValue: false),
            transport,
            postgresPort,
            configuration.GetValue(ClamAvKey, defaultValue: false));
    }
}

/// <summary>Mirrors the API's <c>Messaging:Transport</c> values.</summary>
public enum MessagingTransport
{
    /// <summary>In-process bus. Dev default; not durable.</summary>
    InMemory,

    /// <summary>Durable queues in the application database - no extra container.</summary>
    PostgreSql,

    /// <summary>Adds a RabbitMQ container (+ management UI).</summary>
    RabbitMq
}
