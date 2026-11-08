namespace Shared.Messaging.Extensions;

/// <summary>Message bus settings (<c>Messaging</c> section). See <c>wiki/Architecture/Messaging.md</c>.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>
    /// <see cref="MessagingTransport.InMemory"/> (default: dev / tests - NOT durable, messages in flight are lost on
    /// restart), <see cref="MessagingTransport.PostgreSql"/> (durable queues in the existing database, no new
    /// infrastructure) or <see cref="MessagingTransport.RabbitMq"/>.
    /// </summary>
    public MessagingTransport Transport { get; set; } = MessagingTransport.InMemory;

    public PostgreSqlTransportOptions PostgreSql { get; set; } = new();

    public RabbitMqTransportOptions RabbitMq { get; set; } = new();
}

public enum MessagingTransport
{
    InMemory = 0,
    PostgreSql = 1,
    RabbitMq = 2
}

/// <summary>MassTransit SQL transport on PostgreSQL. Connection: <c>ConnectionStrings:starterkit</c>.</summary>
public sealed class PostgreSqlTransportOptions
{
    /// <summary>Schema holding the transport's queue tables/functions.</summary>
    public string Schema { get; set; } = "transport";

    /// <summary>Role that owns the transport objects; the application user is granted it.</summary>
    public string Role { get; set; } = "transport";

    /// <summary>
    /// Create/upgrade the transport schema, role and functions. Needs DDL + CREATEROLE rights. App instances do it at
    /// startup only with <c>Database:MigrateOnStartup=true</c> (dev, Aspire, tests); in production the one-shot
    /// <c>--migrate-only</c> run owns it, under <c>Database:MigrationConnectionString</c>. Set false only when a DBA
    /// provisions the transport outside the deploy. ADR 0022.
    /// </summary>
    public bool CreateInfrastructure { get; set; } = true;

    /// <summary>Optional privileged user for the startup migration (defaults to the connection-string user).</summary>
    public string? AdminUsername { get; set; }

    public string? AdminPassword { get; set; }
}

public sealed class RabbitMqTransportOptions
{
    public string Host { get; set; } = "localhost";

    public ushort Port { get; set; } = 5672;

    public string VirtualHost { get; set; } = "/";

    public string Username { get; set; } = "guest";

    /// <summary>Secret: Docker secret <c>rabbitmq_password</c> / env <c>Messaging__RabbitMq__Password</c>. Never commit it.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The broker has the <c>rabbitmq_delayed_message_exchange</c> plugin: messages of a migrating tenant are then
    /// redelivered later instead of retried in memory (which holds consumer slots and can stall other tenants), and the
    /// bus gets a message scheduler (saga timeouts, <c>IMessageScheduler</c>); without it RabbitMQ has none.
    /// Off by default because the stock <c>rabbitmq</c> image does not ship the plugin.
    /// </summary>
    public bool UseDelayedExchange { get; set; }
}
