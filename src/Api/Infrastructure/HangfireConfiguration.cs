using Hangfire;
using Hangfire.PostgreSql;
using Shared.Core.Data;

namespace Api.Infrastructure;

public static class HangfireConfiguration
{
    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(Shared.Core.Data.DatabaseDefaults.ConnectionStringName)
            ?? throw new InvalidOperationException("Database connection string 'starterkit' not found");

        // Hangfire installs/upgrades its schema (DDL) on first use. With Database:MigrateOnStartup=false only the
        // one-shot migrator (privileged Database:MigrationConnectionString) may do that; app instances just connect.
        var migration = DatabaseMigrationOptions.From(configuration);
        var prepareSchema = migration.ShouldMigrate;
        if (migration.MigrateOnly && !string.IsNullOrWhiteSpace(migration.MigrationConnectionString))
        {
            connectionString = migration.MigrationConnectionString;
        }

        services.Configure<HangfireDashboardAuthOptions>(
            configuration.GetSection(HangfireDashboardAuthOptions.SectionName));

        services.AddSingleton<TenantJobFilter>();

        services.AddHangfire((sp, config) =>
        {
            config.UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = prepareSchema });
            config.UseRecommendedSerializerSettings();

            // Tenant propagation: the enqueuing flow's tenant is restored while the job runs.
            config.UseFilter(sp.GetRequiredService<TenantJobFilter>());
        });

        // Processing servers, one per workload so a heavy queue never starves another. Each can be switched off with
        // Hangfire:Servers:<Name>:Enabled=false: the SAME image then runs as API-only or worker-only replicas (e.g.
        // docker-compose.worker.yml runs only DataTransfer). Every instance can still enqueue and serve the dashboard.
        var servers = HangfireServerRoles.From(configuration);

        if (servers.Default.Enabled)
        {
            services.AddHangfireServer(options =>
            {
                options.Queues = ["default", "critical"];
                options.WorkerCount = servers.Default.WorkerCount;
                options.ServerName = "main-server";
            });
        }

        // Every notification (all email included) goes through NotificationDispatchJob on this queue.
        if (servers.Notification.Enabled)
        {
            services.AddHangfireServer(options =>
            {
                options.Queues = ["notification"];
                options.WorkerCount = servers.Notification.WorkerCount;
                options.ServerName = "notification-server";
            });
        }

        // Bulk CSV/Excel import/export (DataTransfer module): long, CPU/IO heavy jobs, few workers.
        if (servers.DataTransfer.Enabled)
        {
            services.AddHangfireServer(options =>
            {
                options.Queues = [DataTransfer.Options.DataTransferOptions.Queue];
                options.WorkerCount = servers.DataTransfer.WorkerCount;
                options.ServerName = "data-transfer-server";
            });
        }

#if (AGENTIC)
        // AI agent runs (Agentic module): a worker is held for the length of a model request, so they get their own
        // server and never delay notifications or the default queue.
        if (servers.Agentic.Enabled)
        {
            services.AddHangfireServer(options =>
            {
                options.Queues = [Agentic.Options.AgenticOptions.Queue];
                options.WorkerCount = servers.Agentic.WorkerCount;
                options.ServerName = "agentic-server";
            });
        }
#endif

        return services;
    }
}

/// <summary>One Hangfire processing server (section <c>Hangfire:Servers:&lt;Name&gt;</c>).</summary>
public sealed class HangfireServerRole
{
    public bool Enabled { get; set; } = true;

    public int WorkerCount { get; set; }
}

/// <summary>
/// Which Hangfire servers this instance runs (<c>Hangfire:Servers</c>). All are on by default (single box, dev,
/// tests). Turn a server off on the API replicas and on in a worker replica of the same image to move that workload.
/// </summary>
public sealed class HangfireServerRoles
{
    public HangfireServerRole Default { get; set; } = new() { WorkerCount = 5 };
    public HangfireServerRole Notification { get; set; } = new() { WorkerCount = 4 };
    public HangfireServerRole DataTransfer { get; set; } = new() { WorkerCount = 2 };
#if (AGENTIC)
    public HangfireServerRole Agentic { get; set; } = new() { WorkerCount = 4 };
#endif

    public static HangfireServerRoles From(IConfiguration configuration)
    {
        var roles = new HangfireServerRoles();
        configuration.GetSection("Hangfire:Servers").Bind(roles);
        List<HangfireServerRole> all = [roles.Default, roles.Notification, roles.DataTransfer];
#if (AGENTIC)
        all.Add(roles.Agentic);
#endif
        foreach (var role in all)
        {
            if (role.WorkerCount < 1)
                throw new InvalidOperationException("Hangfire:Servers:<Name>:WorkerCount must be at least 1.");
        }

        return roles;
    }
}
