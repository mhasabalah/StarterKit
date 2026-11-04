using Shared.Core.Data;

namespace Shared.Core.MultiTenancy.BackgroundJobs;

/// <summary>
/// Per-tenant fan-out for recurring/background work on TENANT DATA. Each tenant runs in its own
/// <see cref="IUnitOfWorkRunner"/> unit inside its tenant scope (right database, filters, RLS), and one tenant's
/// failure is logged and skipped - it never aborts the others. Single-tenant mode visits only the Default tenant.
/// <example><code>
/// await tenantJobs.ForEachTenantAsync(async (services, ct) =>
/// {
///     var db = services.GetRequiredService&lt;CatalogDbContext&gt;();
///     ...
/// }, nameof(PurgeSoftDeletedProductsJob), cancellationToken);
/// </code></example>
/// </summary>
public interface ITenantJobRunner
{
    /// <returns>Number of tenants whose work failed.</returns>
    Task<int> ForEachTenantAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        string jobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> once per serviceable tenant inside its <see cref="TenantScope"/> only (no unit of
    /// work) - for job bodies that already manage their own units of work / mediator commands. Failures are logged
    /// per tenant and counted; the other tenants still run.
    /// </summary>
    /// <returns>Number of tenants whose work failed.</returns>
    Task<int> ForEachTenantScopeAsync(Func<TenantInfo, CancellationToken, Task> work, string jobName,
        CancellationToken cancellationToken = default);

    /// <summary>One unit of work as <paramref name="tenantId"/> (looked up; throws when it does not exist).</summary>
    Task RunForTenantAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}

public sealed class TenantJobRunner(
    ITenantStore tenantStore,
    IUnitOfWorkRunner unitOfWork,
    ILogger<TenantJobRunner> logger) : ITenantJobRunner
{
    public async Task<int> ForEachTenantAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var tenants = await tenantStore.GetServiceableTenantsAsync(cancellationToken);
        var failures = 0;

        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (TenantScope.Begin(tenant))
            using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.Id }))
            {
                try
                {
                    await unitOfWork.RunAsync(work, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    logger.LogError(ex, "{Job} failed for tenant {TenantIdentifier} ({TenantId}); continuing with the next tenant",
                        jobName, tenant.Identifier, tenant.Id);
                }
            }
        }

        return failures;
    }

    public async Task<int> ForEachTenantScopeAsync(Func<TenantInfo, CancellationToken, Task> work, string jobName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var failures = 0;

        foreach (var tenant in await tenantStore.GetServiceableTenantsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (TenantScope.Begin(tenant))
            using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenant.Id }))
            {
                try
                {
                    await work(tenant, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    logger.LogError(ex, "{Job} failed for tenant {TenantIdentifier} ({TenantId}); continuing with the next tenant",
                        jobName, tenant.Identifier, tenant.Id);
                }
            }
        }

        return failures;
    }

    public async Task RunForTenantAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantStore.FindByIdAsync(tenantId, cancellationToken)
                     ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");

        using (TenantScope.Begin(tenant))
        {
            await unitOfWork.RunAsync(work, cancellationToken);
        }
    }
}
