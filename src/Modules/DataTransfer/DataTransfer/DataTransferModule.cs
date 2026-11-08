using DataTransfer.Domains.TransferJobs.Jobs;
using DataTransfer.Domains.TransferJobs.Services;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Notification.Contracts.Realtime;
using Shared.Core.Entitlements;
using Shared.Core.HealthCheck;
using Shared.Core.Messaging.Outbox;

namespace DataTransfer;

/// <summary>
/// Bulk CSV/Excel import and export (tenant-data module). Owning modules plug in through
/// <c>AddImportDefinition</c> / <c>AddExportDefinition</c> (DataTransfer.Contracts). Program.cs must also pass
/// <c>DataTransferRoot.AssemblyName</c> to <c>AddMassTransitWithAssemblies</c> and
/// <c>DataTransferContractsRoot.AssemblyName</c> to <c>AddEnumLookup</c>; the host runs the jobs on the Hangfire
/// server of queue <see cref="DataTransferOptions.Queue"/> (<c>Hangfire:Servers:DataTransfer</c>).
/// See wiki/Modules/DataTransfer.md.
/// </summary>
public static class DataTransferModule
{
    public const string SweepJobId = "datatransfer-requeue-stalled";
    public const string PurgeJobId = "datatransfer-purge-expired";

    public static IServiceCollection AddDataTransferModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DataTransferOptions>(configuration.GetSection(DataTransferOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddMetrics();

        services.AddScoped<ITransferJobRepository, TransferJobRepository>();
        services.AddScoped<TransferDefinitionRegistry>();
        services.AddScoped<TransferAccess>();
        services.AddSingleton<TransferFileStore>();
        services.AddSingleton<DataTransferMetrics>();
        services.AddSingleton<TransferProgressPublisher>();
        // Live progress goes through the Notification hub; without that module (or with real time off) it is a no-op.
        services.TryAddSingleton<IUserEventPublisher, NullUserEventPublisher>();
        // Plan gates and the row quota: permissive defaults unless a billing module registers the real ones.
        services.AddEntitlements();

        services.AddMediator(DataTransferRoot.AssemblyName);
        services.AddCarterWithAssemblies(DataTransferRoot.AssemblyName);
        services.AddCustomDbContext<DataTransferDbContext>(configuration, Shared.Core.Data.DatabaseDefaults.ConnectionStringName);
        services.AddOutbox<DataTransferDbContext>(); // DataImportCompletedIntegrationEvent

        // Degraded (never unready) when no live server processes the data-transfer queue.
        services.AddHealthChecks().AddCheck<DataTransferQueueHealthCheck>(
            DataTransferQueueHealthCheck.Name,
            failureStatus: HealthStatus.Degraded,
            tags: [HealthCheckTags.Ready]);

        // Hangfire jobs
        services.AddTransient<ProcessImportJob>();
        services.AddTransient<ProcessExportJob>();
        services.AddTransient<RequeueStalledTransferJobsJob>();
        services.AddTransient<PurgeExpiredTransferJobsJob>();

        return services;
    }

    public static IApplicationBuilder UseDataTransferModule(this IApplicationBuilder app)
    {
        app.UseMigration<DataTransferDbContext>();

        using var scope = app.ApplicationServices.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DataTransferOptions>>().Value;
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

        // Fail fast on duplicate definition keys (a wiring bug in an owning module).
        _ = scope.ServiceProvider.GetRequiredService<TransferDefinitionRegistry>();

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Hangfire:Servers:DataTransfer:Enabled", true))
        {
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DataTransferModule))
                .LogWarning("This instance does not process the '{Queue}' queue (Hangfire:Servers:DataTransfer:Enabled=false); " +
                            "a worker replica must run it, see the '{Check}' health check", DataTransferOptions.Queue,
                    DataTransferQueueHealthCheck.Name);
        }

        recurringJobManager.AddOrUpdate<RequeueStalledTransferJobsJob>(
            SweepJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.SweepCron);

        recurringJobManager.AddOrUpdate<PurgeExpiredTransferJobsJob>(
            PurgeJobId,
            x => x.ExecuteAsync(JobCancellationToken.Null),
            options.PurgeCron);

        return app;
    }
}
