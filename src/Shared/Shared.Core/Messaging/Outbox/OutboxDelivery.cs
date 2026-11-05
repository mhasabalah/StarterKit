using Microsoft.Extensions.Options;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.BackgroundJobs;
using Shared.Core.OpenTelemetryCollector.DiagnosticsProvider;
using System.Diagnostics.Metrics;

namespace Shared.Core.Messaging.Outbox;

/// <summary>Contexts that own an outbox (<c>services.AddOutbox&lt;TContext&gt;()</c>), visited by the sweep job.</summary>
public sealed class OutboxRegistry
{
    private readonly List<Type> _contextTypes = [];

    public IReadOnlyList<Type> ContextTypes => _contextTypes;

    internal void Add(Type contextType)
    {
        if (!_contextTypes.Contains(contextType))
            _contextTypes.Add(contextType);
    }
}

/// <summary>
/// Immediate delivery: runs once the rows of a unit of work are COMMITTED (the notification is staged in the domain
/// event collector and only published for committed saves, after the outermost unit of work). Delivers exactly those
/// rows in a fresh unit of work, inside the tenant scope of the save (same database, same isolation).
/// <para>
/// Never fails the caller: the business change is already committed and its events are safe in the outbox. A failure
/// is logged; <see cref="OutboxSweepJob"/> retries.
/// </para>
/// </summary>
public sealed class OutboxMessagesSavedHandler(IUnitOfWorkRunner runner, ILogger<OutboxMessagesSavedHandler> logger)
    : INotificationHandler<OutboxMessagesSaved>
{
    public async Task Handle(OutboxMessagesSaved notification, CancellationToken cancellationToken)
    {
        if (notification.MessageIds.Count == 0)
            return;

        try
        {
            using (notification.IsHostScope
                       ? TenantScope.BeginHostScope(notification.Tenant)
                       : notification.Tenant is null ? TenantScope.BeginEmpty() : TenantScope.Begin(notification.Tenant))
            {
                await runner.RunAsync((services, ct) =>
                {
                    var context = (DbContext)services.GetRequiredService(notification.ContextType);
                    return services.GetRequiredService<OutboxRelay>().DeliverAsync(context, notification.MessageIds, ct);
                }, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Immediate outbox delivery of {Count} message(s) from {Context} failed; the outbox sweep retries them",
                notification.MessageIds.Count, notification.ContextType.Name);
        }
    }
}

/// <summary>
/// Safety net + housekeeping (Hangfire recurring job, <see cref="OutboxOptions.SweepCron"/>): delivers every pending,
/// due row of every outbox context (rows whose immediate delivery failed, was interrupted by a crash, or never ran
/// because the save happened outside a unit of work) and deletes delivered rows past retention.
/// <list type="bullet">
/// <item>Host contexts: one unit of work in host scope.</item>
/// <item>Tenant-data contexts: one unit of work per serviceable tenant (its database, its filters/RLS) via
/// <see cref="ITenantJobRunner"/>; a migrating tenant is skipped until it is serviceable again.</item>
/// </list>
/// One context's or tenant's failure is logged and never stops the others.
/// </summary>
public sealed class OutboxSweepJob(
    OutboxRegistry registry,
    IUnitOfWorkRunner runner,
    ITenantJobRunner tenantJobs,
    OutboxMetrics metrics,
    ILogger<OutboxSweepJob> logger)
{
    public const string RecurringJobId = "outbox-sweep";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        foreach (var contextType in registry.ContextTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (contextType.IsAssignableTo(typeof(ITenantDataContext)))
            {
                // Summed over the tenants (each in its own database / filters); a failed tenant is logged and missing.
                long pending = 0, deadLettered = 0;
                await tenantJobs.ForEachTenantScopeAsync(async (_, ct) =>
                {
                    var backlog = await SweepAsync(contextType, ct);
                    pending += backlog.Pending;
                    deadLettered += backlog.DeadLettered;
                }, $"{nameof(OutboxSweepJob)}:{contextType.Name}", cancellationToken);
                metrics.Record(contextType.Name, pending, deadLettered);
                continue;
            }

            try
            {
                using (TenantScope.BeginHostScope())
                {
                    var backlog = await SweepAsync(contextType, cancellationToken);
                    metrics.Record(contextType.Name, backlog.Pending, backlog.DeadLettered);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox sweep of {Context} failed; retried on the next run", contextType.Name);
            }
        }
    }

    private Task<(long Pending, long DeadLettered)> SweepAsync(Type contextType, CancellationToken cancellationToken) =>
        runner.RunAsync(async (services, ct) =>
        {
            var context = (DbContext)services.GetRequiredService(contextType);
            var relay = services.GetRequiredService<OutboxRelay>();

            var published = await relay.DeliverAsync(context, null, ct);
            var purged = await relay.PurgeDeliveredAsync(context, ct);

            if (published > 0 || purged > 0)
            {
                logger.LogInformation("Outbox sweep {Context}: {Published} published, {Purged} delivered row(s) purged",
                    contextType.Name, published, purged);
            }

            return await OutboxRelay.CountBacklogAsync(context, ct);
        }, cancellationToken);
}

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/>'s outbox for delivery (immediate + sweep). The context must map it
    /// (<c>builder.AddOutbox()</c> / <c>builder.AddTenantOutbox()</c>). Idempotent.
    /// </summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddOutboxCore();
        RegistryOf(services).Add(typeof(TContext));
        return services;
    }

    /// <summary>Shared outbox services (idempotent). Called by <see cref="AddOutbox{TContext}"/> and <c>AddCustomDbContext</c>.</summary>
    public static IServiceCollection AddOutboxCore(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(OutboxRegistry)))
            return services;

        services.AddSingleton(new OutboxRegistry());
        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<OutboxInterceptor>();
        services.AddScoped<OutboxRelay>();
        services.AddScoped<OutboxSweepJob>();
        services.AddSingleton(sp => new OutboxMetrics(
            sp.GetService<IDiagnosticsProvider>()?.Meter ?? new Meter("StarterKit.Outbox"),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<OutboxAdminService>();
        // Explicit (the mediator only scans module assemblies): immediate delivery after commit.
        services.AddTransient<INotificationHandler<OutboxMessagesSaved>, OutboxMessagesSavedHandler>();
        return services;
    }

    private static OutboxRegistry RegistryOf(IServiceCollection services) =>
        (OutboxRegistry)services.Single(d => d.ServiceType == typeof(OutboxRegistry)).ImplementationInstance!;
}
