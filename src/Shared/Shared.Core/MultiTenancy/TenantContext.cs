using Microsoft.Extensions.Options;

namespace Shared.Core.MultiTenancy;

/// <summary>
/// The tenant of the current execution flow (HTTP request, Hangfire job, consumer, <c>IUnitOfWorkRunner</c> unit).
/// Read-only; the ambient value is set with <see cref="TenantScope"/>.
/// <para>
/// When multi-tenancy is disabled and no explicit scope is active, the tenant is
/// <see cref="TenantDefaults.DefaultTenant"/> - so single-tenant apps never have an "unresolved" tenant.
/// </para>
/// </summary>
public interface ITenantContext
{
    /// <summary><c>MultiTenancy:Enabled</c>.</summary>
    bool MultiTenancyEnabled { get; }

    /// <summary>The current tenant, or <c>null</c> when none is resolved (only possible when multi-tenancy is on).</summary>
    TenantInfo? Tenant { get; }

    Guid? TenantId => Tenant?.Id;

    bool IsResolved => Tenant is not null;

    /// <summary>
    /// Platform ("host") scope: tenant filters / RLS are bypassed. Only entered explicitly via
    /// <see cref="TenantScope.BeginHostScope"/> (migrations, cross-tenant maintenance, platform admin queries).
    /// </summary>
    bool IsHostScope { get; }

    /// <summary>Branches visible to the current flow.</summary>
    BranchScope Branches { get; }

    /// <summary>The caller's membership in <see cref="Tenant"/> (HTTP requests by members only).</summary>
    TenantMembershipInfo? Membership { get; }
}

/// <summary>Ambient-backed <see cref="ITenantContext"/> (singleton; reads the AsyncLocal <see cref="TenantScope"/>).</summary>
public sealed class TenantContext(IOptions<MultiTenancyOptions> options) : ITenantContext
{
    /// <summary>A single-tenant (disabled) context: the Default tenant unless a scope says otherwise. For tests/tools.</summary>
    public static ITenantContext SingleTenant { get; } =
        new TenantContext(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions()));

    public bool MultiTenancyEnabled => options.Value.Enabled;

    public TenantInfo? Tenant
    {
        get
        {
            var state = TenantScope.Current;
            if (state is not null)
            {
                return state.Tenant;
            }

            return MultiTenancyEnabled ? null : TenantDefaults.DefaultTenant;
        }
    }

    public bool IsHostScope => TenantScope.Current?.IsHost ?? false;

    public BranchScope Branches => TenantScope.Current?.Branches ?? BranchScope.Unrestricted;

    public TenantMembershipInfo? Membership => TenantScope.Current?.Membership;
}

/// <summary>
/// The AsyncLocal ambient tenant (same model as <c>IHttpContextAccessor</c>). Scopes nest; disposing restores the
/// previous value. Set by the tenant middleware, the Hangfire/MassTransit filters and the tenant-aware
/// <c>IUnitOfWorkRunner</c> extensions - application code rarely calls it directly.
/// </summary>
public static class TenantScope
{
    private static readonly AsyncLocal<TenantScopeState?> Ambient = new();

    public static TenantScopeState? Current => Ambient.Value;

    /// <summary>Runs the flow as <paramref name="tenant"/>.</summary>
    public static IDisposable Begin(
        TenantInfo tenant,
        BranchScope? branches = null,
        TenantMembershipInfo? membership = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return Push(new TenantScopeState(tenant, branches ?? membership?.Branches ?? BranchScope.Unrestricted,
            membership, IsHost: false));
    }

    /// <summary>
    /// Runs the flow in platform scope: tenant filters and RLS are bypassed. Keep these blocks small, explicit and
    /// audited; never wrap request handling in it.
    /// </summary>
    /// <param name="database">
    /// Optional tenant whose DATABASE to use (a dedicated tenant's database for migrations/maintenance/purge);
    /// filters and RLS are still bypassed. <c>null</c> = the shared database.
    /// </param>
    public static IDisposable BeginHostScope(TenantInfo? database = null) =>
        Push(new TenantScopeState(database, BranchScope.Unrestricted, null, IsHost: true));

    /// <summary>Explicitly "no tenant" (e.g. to make a tenant-less HTTP request fail closed).</summary>
    public static IDisposable BeginEmpty() =>
        Push(new TenantScopeState(null, BranchScope.Unrestricted, null, IsHost: false));

    private static IDisposable Push(TenantScopeState state)
    {
        var previous = Ambient.Value;
        Ambient.Value = state;
        return new Restore(previous);
    }

    private sealed class Restore(TenantScopeState? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}

/// <summary>One ambient tenant scope.</summary>
public sealed record TenantScopeState(
    TenantInfo? Tenant,
    BranchScope Branches,
    TenantMembershipInfo? Membership,
    bool IsHost);
