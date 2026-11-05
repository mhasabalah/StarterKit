using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy;

namespace Identity.Services.Sso;

/// <summary>
/// "Does the tenant that owns this connection have the plan feature <c>sso</c>?" Evaluated in that tenant's scope
/// (sign-in and enforcement run before any tenant is resolved). ONE check for every SSO path: sign-in, discovery
/// and enforcement. Enforcement must lapse together with sign-in - otherwise a plan downgrade would block
/// passwords while also refusing SSO, locking every user of the domain out.
/// </summary>
public interface ISsoEntitlement
{
    Task<bool> IsEntitledAsync(Guid tenantId, CancellationToken ct);
}

public sealed class SsoEntitlement(ITenantStore tenantStore, IEntitlementService entitlements) : ISsoEntitlement
{
    public async Task<bool> IsEntitledAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await tenantStore.FindByIdAsync(tenantId, ct);
        if (tenant is null)
            return false;

        using (TenantScope.Begin(tenant))
        {
            return await entitlements.HasFeatureAsync(EntitlementKeys.Sso, ct);
        }
    }
}
