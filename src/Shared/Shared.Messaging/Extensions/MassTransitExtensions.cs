using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Messaging;

namespace Shared.Messaging.Extensions;

public static class MassTransitExtensions
{
    /// <summary>
    /// Registers MassTransit ONCE for the whole host: consumers/sagas of <paramref name="assemblies"/>, the transport
    /// chosen by <c>Messaging:Transport</c> (<see cref="MessagingOptions"/>), tenant propagation filters and the shared
    /// retry policy. Publishing of integration events goes through the transactional outbox
    /// (<c>Shared.Core.Messaging.Outbox</c>), never directly from feature code.
    /// </summary>
    public static IServiceCollection AddMassTransitWithAssemblies(
        this IServiceCollection services, IConfiguration configuration, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var messaging = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>()
                        ?? new MessagingOptions();
        if (!Enum.IsDefined(messaging.Transport))
        {
            throw new InvalidOperationException(
                $"Messaging:Transport '{messaging.Transport}' is not supported (InMemory | PostgreSql | RabbitMq).");
        }

        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));

        // The host is "started" only once every receive endpoint is bound. Otherwise anything published right after
        // startup (the outbox relay, a request) can reach a topic whose subscription does not exist yet: the
        // PostgreSql transport accepts it, routes it to nobody, and the outbox marks it delivered - a silent loss.
        services.AddOptions<MassTransitHostOptions>().Configure(o =>
        {
            o.WaitUntilStarted = true;
            o.StartTimeout = TimeSpan.FromMinutes(1);
            o.StopTimeout = TimeSpan.FromSeconds(30);
        });

        if (messaging.Transport == MessagingTransport.PostgreSql)
        {
            ConfigurePostgresTransport(services, configuration, messaging.PostgreSql);
        }

        services.AddMassTransit(config =>
        {
            config.SetKebabCaseEndpointNameFormatter();
            // No saga exists yet. The first real saga needs a durable repository (EF Core saga repository).
            config.SetInMemorySagaRepositoryProvider();

            config.AddConsumers(assemblies);
            config.AddSagaStateMachines(assemblies);
            config.AddSagas(assemblies);
            config.AddActivities(assemblies);

            switch (messaging.Transport)
            {
                case MessagingTransport.PostgreSql:
                    config.UsingPostgres((context, cfg) => ConfigureBus(context, cfg, delayedRedelivery: true));
                    break;

                case MessagingTransport.RabbitMq:
                    var rabbit = messaging.RabbitMq;
                    // Aspire (WithReference(rabbitmq)) provides ConnectionStrings:rabbitmq as an amqp:// URI with
                    // credentials; otherwise the explicit Messaging:RabbitMq:* settings are used.
                    var rabbitConnectionString = configuration.GetConnectionString("rabbitmq");
                    if (string.IsNullOrWhiteSpace(rabbitConnectionString) && string.IsNullOrWhiteSpace(rabbit.Host))
                        throw new InvalidOperationException(
                            "The RabbitMq transport needs ConnectionStrings:rabbitmq or Messaging:RabbitMq:Host.");

                    config.UsingRabbitMq((context, cfg) =>
                    {
                        if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
                        {
                            cfg.Host(new Uri(rabbitConnectionString));
                        }
                        else
                        {
                            cfg.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, h =>
                            {
                                h.Username(rabbit.Username);
                                h.Password(rabbit.Password);
                            });
                        }

                        if (rabbit.UseDelayedExchange)
                        {
                            cfg.UseDelayedMessageScheduler();
                        }

                        ConfigureBus(context, cfg, delayedRedelivery: rabbit.UseDelayedExchange);
                    });
                    break;

                default:
                    config.UsingInMemory((context, cfg) => ConfigureBus(context, cfg, delayedRedelivery: true));
                    break;
            }
        });

        return services;
    }

    /// <summary>Transport-independent bus configuration (identical for every transport, so behavior never drifts).</summary>
    private static void ConfigureBus<TEndpoint>(IBusRegistrationContext context, IBusFactoryConfigurator<TEndpoint> cfg,
        bool delayedRedelivery)
        where TEndpoint : IReceiveEndpointConfigurator
    {
        // OUTER: a tenant whose data is being moved defers its messages (TenantConsumeFilter throws
        // TenantMigratingException) for up to ~1 hour. Delayed REDELIVERY hands the message back to the transport, so
        // it does not occupy a consumer slot while it waits. An in-memory retry holds one slot per waiting message and
        // starves every other tenant on the endpoint (TenantMessagingBusTests.A_migrating_tenant_does_not_block_other_tenants).
        // InMemory and PostgreSql deliver delayed messages natively; RabbitMQ needs the delayed-message exchange plugin
        // (Messaging:RabbitMq:UseDelayedExchange), otherwise it falls back to the in-memory retry and its starvation risk.
        if (delayedRedelivery)
        {
            cfg.UseDelayedRedelivery(r =>
            {
                r.Handle<TenantMigratingException>();
                r.Intervals(
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30));
            });
        }
        else
        {
            cfg.UseMessageRetry(r =>
            {
                r.Handle<TenantMigratingException>();
                r.Exponential(12, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
            });
        }

        // INNER: transient consumer failures (database blips, lock timeouts...). After the last attempt the message
        // faults: durable transports move it to the endpoint's _error queue for inspection/replay.
        cfg.UseMessageRetry(r =>
        {
            r.Ignore<TenantMigratingException>();
            r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
        });

        // Tenant propagation: the ambient tenant travels as a header and is restored for consumers.
        cfg.UsePublishFilter(typeof(TenantPublishFilter<>), context);
        cfg.UseSendFilter(typeof(TenantSendFilter<>), context);
        cfg.UseConsumeFilter(typeof(TenantConsumeFilter<>), context);

        cfg.ConfigureEndpoints(context);
    }

    private static void ConfigurePostgresTransport(IServiceCollection services, IConfiguration configuration,
        PostgreSqlTransportOptions options)
    {
        // Same database as the modules: ConnectionStrings:starterkit (Aspire) or PostgresOptions:ConnectionString.
        var connectionString = configuration.GetConnectionString(Shared.Core.Data.DatabaseDefaults.ConnectionStringName)
                               ?? configuration[$"{nameof(PostgresOptions)}:{nameof(PostgresOptions.ConnectionString)}"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Messaging:Transport=PostgreSql needs ConnectionStrings:starterkit (or PostgresOptions:ConnectionString).");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // Who may create the transport schema/role (DDL + CREATE ROLE): see Database:MigrateOnStartup. The one-shot
        // migrator (--migrate-only) uses the privileged Database:MigrationConnectionString when one is configured.
        var database = DatabaseMigrationOptions.From(configuration);
        var admin = database.MigrateOnly && !string.IsNullOrWhiteSpace(database.MigrationConnectionString)
            ? new NpgsqlConnectionStringBuilder(database.MigrationConnectionString)
            : builder;

        services.AddOptions<SqlTransportOptions>().Configure(o =>
        {
            o.Host = builder.Host;
            o.Port = builder.Port;
            o.Database = builder.Database;
            o.Username = builder.Username;
            o.Password = builder.Password;
            o.Schema = options.Schema;
            o.Role = options.Role;
            o.AdminUsername = string.IsNullOrWhiteSpace(options.AdminUsername) ? admin.Username : options.AdminUsername;
            o.AdminPassword = string.IsNullOrWhiteSpace(options.AdminUsername) ? admin.Password : options.AdminPassword;
        });

        if (!options.CreateInfrastructure)
        {
            return;
        }

        if (database.MigrateOnStartup && !database.MigrateOnly)
        {
            // Boot-time migration (dev, Aspire, tests). Must be registered before the bus hosted service: creates/upgrades
            // the transport schema first.
            services.AddPostgresMigrationHostedService(create: true, delete: false);
        }

        // Otherwise (production): app instances with MigrateOnStartup=false never run DDL / CREATE ROLE; the one-shot
        // migrator does it explicitly through MigrateMessagingTransportAsync (hosted services never run in --migrate-only).
    }

    /// <summary>
    /// Creates/upgrades the PostgreSql transport schema, role and functions explicitly. For the <c>--migrate-only</c>
    /// run, where hosted services never start. A no-op unless the PostgreSql transport with CreateInfrastructure is on.
    /// </summary>
    /// <returns>True when the transport infrastructure was migrated.</returns>
    public static async Task<bool> MigrateMessagingTransportAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var messaging = services.GetService<IOptions<MessagingOptions>>()?.Value;
        if (messaging is not { Transport: MessagingTransport.PostgreSql } || !messaging.PostgreSql.CreateInfrastructure)
        {
            return false;
        }

        // Run the very same hosted service the boot-time path registers (same steps, same order), against the already
        // configured transport options, in a private container so it never runs in an app instance.
        var isolated = new ServiceCollection();
        isolated.AddLogging();
        isolated.AddSingleton(services.GetRequiredService<IOptions<SqlTransportOptions>>());
        isolated.AddPostgresMigrationHostedService(create: true, delete: false);
        await using var provider = isolated.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(cancellationToken);
        }

        return true;
    }
}
