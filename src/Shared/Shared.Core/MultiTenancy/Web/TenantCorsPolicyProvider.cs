using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Shared.Core.MultiTenancy.Web;

/// <summary>
/// CORS policy provider that, on top of the configured policy (<c>Cors:AllowedOrigins</c>), allows the browser
/// origins OF TENANTS (<c>MultiTenancy:Cors</c>):
/// <list type="bullet">
/// <item><c>https://{identifier}.{FrontendRootDomain}</c> of an existing, not-deleted tenant;</item>
/// <item><c>https://{host}</c> of a tenant's VERIFIED custom domain (unverified domains never get CORS).</item>
/// </list>
/// The origin is added to a per-request copy of the policy (credentials/methods/headers unchanged), so the CORS
/// middleware echoes exactly that origin - never <c>*</c>. Lookups use the cached <see cref="ITenantStore"/> (misses
/// are cached too). Plain <c>http</c> origins are accepted in Development only.
/// <para>CORS is a browser read-protection, not authorization: tenant isolation is still enforced per request.</para>
/// </summary>
public sealed class TenantCorsPolicyProvider(
    IOptions<CorsOptions> corsOptions,
    IOptions<MultiTenancyOptions> tenancyOptions,
    ITenantStore store,
    IHostEnvironment environment) : ICorsPolicyProvider
{
    private readonly DefaultCorsPolicyProvider _inner = new(corsOptions);

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var policy = await _inner.GetPolicyAsync(context, policyName);
        var options = tenancyOptions.Value;
        if (policy is null || !options.Enabled || !options.Cors.Enabled || policy.AllowAnyOrigin) return policy;

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || policy.IsOriginAllowed(origin)) return policy;

        if (!await IsTenantOriginAsync(origin, options.Cors, context.RequestAborted)) return policy;

        // CorsPolicyBuilder(policy) copies the ORIGINAL IsOriginAllowed delegate (bound to the original origin list),
        // so the tenant origin must be allowed explicitly as well.
        var tenantPolicy = new CorsPolicyBuilder(policy).WithOrigins(origin).Build();
        var original = policy.IsOriginAllowed;
        tenantPolicy.IsOriginAllowed = o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase) || original(o);
        return tenantPolicy;
    }

    internal async Task<bool> IsTenantOriginAsync(string origin, TenantCorsOptions cors, CancellationToken ct)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || uri.UserInfo.Length > 0)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && environment.IsDevelopment()))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();

        var root = cors.FrontendRootDomain?.Trim().TrimEnd('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(root) && host.EndsWith("." + root, StringComparison.Ordinal))
        {
            var label = host[..^(root.Length + 1)];
            if (label.Length == 0 || label.Contains('.')) return false;

            var tenant = await store.FindByIdentifierAsync(label, ct);
            return tenant is { Status: not TenantStatus.Deleted };
        }

        if (!cors.AllowVerifiedCustomDomains) return false;

        // FindByHostAsync only knows VERIFIED custom domains.
        var byDomain = await store.FindByHostAsync(host, ct);
        return byDomain is { Status: not TenantStatus.Deleted };
    }
}
