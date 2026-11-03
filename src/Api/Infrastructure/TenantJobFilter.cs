using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Shared.Core.MultiTenancy;

namespace Api.Infrastructure;

/// <summary>
/// Carries the ambient tenant into Hangfire jobs: <see cref="OnCreating"/> stores the enqueuing flow's tenant as a job
/// parameter; <see cref="OnPerforming"/> restores it as the job's <see cref="TenantScope"/> (the job's DI scope,
/// DbContexts, filters and RLS then target that tenant). Jobs enqueued without a tenant (recurring jobs) run
/// tenant-less and must fan out with <c>ITenantJobRunner</c>.
/// </summary>
public sealed class TenantJobFilter(ITenantContext tenantContext, ITenantStore tenantStore, ILogger<TenantJobFilter> logger)
    : JobFilterAttribute, IClientFilter, IServerFilter
{
    public const string TenantParameter = "TenantId";
    public const string HostScopeParameter = "TenantHostScope";
    private const string ScopeItem = "TenantJobFilter.Scope";

    public void OnCreating(CreatingContext context)
    {
        if (context.Parameters.ContainsKey(TenantParameter)) return; // explicit wins

        if (tenantContext.TenantId is { } tenantId)
        {
            context.SetJobParameter(TenantParameter, tenantId);
        }

        if (tenantContext.IsHostScope)
        {
            context.SetJobParameter(HostScopeParameter, true);
        }
    }

    public void OnCreated(CreatedContext context)
    {
    }

    public void OnPerforming(PerformingContext context)
    {
        var tenantId = context.GetJobParameter<Guid?>(TenantParameter);
        var hostScope = context.GetJobParameter<bool?>(HostScopeParameter) == true;

        if (tenantId is null)
        {
            if (hostScope)
            {
                context.Items[ScopeItem] = TenantScope.BeginHostScope();
            }

            return;
        }

        var tenant = tenantStore.FindByIdAsync(tenantId.Value).GetAwaiter().GetResult();
        if (tenant is null)
        {
            logger.LogWarning("Cancelling job {JobId}: tenant {TenantId} no longer exists", context.BackgroundJob.Id, tenantId);
            context.Canceled = true;
            return;
        }

        if (tenant.Status == TenantStatus.Migrating && !hostScope)
        {
            // The tenant's data is being moved: writes would land in the database being left. Failing the attempt
            // lets Hangfire's retry (back-off) run the job after the move.
            logger.LogInformation("Postponing job {JobId}: tenant {TenantId} is migrating", context.BackgroundJob.Id, tenantId);
            throw new TenantMigratingException();
        }

        context.Items[ScopeItem] = hostScope ? TenantScope.BeginHostScope(tenant) : TenantScope.Begin(tenant);
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(ScopeItem, out var scope) && scope is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
