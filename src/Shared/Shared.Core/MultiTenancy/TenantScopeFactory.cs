using Microsoft.Extensions.Options;

namespace Shared.Core.MultiTenancy;

/// <summary>
/// Finds the tenant to act for when work is done on its behalf from code that runs in another (or no) scope: an event
/// handler or job notifying a tenant's subscriber. The CALLER enters the scope - <c>using (TenantScope.Begin(tenant))</c>
/// in its own async method (an ambient scope set inside a callee's async method would not flow back) - so anything
/// enqueued inside (notifications, Hangfire jobs) carries that tenant, e.g. inbox rows get the right <c>tenant_id</c>.
/// Null = nothing to enter: single-tenant mode, empty id or unknown tenant.
/// </summary>
public interface ITenantScopeFactory
{
    /// <summary>True when multi-tenancy is on (then the tenant id is meaningful to clients).</summary>
    bool IsMultiTenant { get; }

    Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class TenantScopeFactory(ITenantStore store, IOptions<MultiTenancyOptions> options) : ITenantScopeFactory
{
    public bool IsMultiTenant => options.Value.Enabled;

    public async Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        !options.Value.Enabled || tenantId == Guid.Empty ? null : await store.FindByIdAsync(tenantId, cancellationToken);
}

/// <summary>For hosts without multi-tenancy wiring (tests, tools).</summary>
public sealed class NoOpTenantScopeFactory : ITenantScopeFactory
{
    public bool IsMultiTenant => false;

    public Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantInfo?>(null);
}
