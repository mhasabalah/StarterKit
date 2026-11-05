using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shared.Core.MultiTenancy;

namespace Shared.Core.Web;

public class HttpContextService : IHttpContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string CorrelationIdHeaderKey = CorrelationExtensions.HeaderName;

    public HttpContextService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext? Context => _httpContextAccessor.HttpContext;
    private ClaimsPrincipal? User => Context?.User;

    public string? GetUserId()
    {
        return User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public long? GetUserIdAsLong()
    {
        var userId = GetUserId();
        return long.TryParse(userId, out var id) ? id : null;
    }

    public Guid? GetUserIdAsGuid()
    {
        var userId = GetUserId();
        return Guid.TryParse(userId, out var id) ? id : null;
    }

    public string? GetUserName()
    {
        return User?.FindFirstValue(ClaimTypes.Name)
               ?? User?.FindFirstValue("username")
               ?? User?.Identity?.Name;
    }

    public string? GetEmail()
    {
        return User?.FindFirstValue(ClaimTypes.Email)
               ?? User?.FindFirstValue("email");
    }

    public IEnumerable<string> GetRoles()
    {
        if (User == null)
            return Enumerable.Empty<string>();

        return User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Union(User.FindAll("role").Select(c => c.Value))
            .Distinct();
    }

    public bool IsAuthenticated()
    {
        return User?.Identity?.IsAuthenticated ?? false;
    }

    public string? GetClaimValue(string claimType)
    {
        return User?.FindFirstValue(claimType);
    }

    public IEnumerable<string> GetClaimValues(string claimType)
    {
        if (User == null)
            return Enumerable.Empty<string>();

        return User.FindAll(claimType).Select(c => c.Value);
    }

    public IEnumerable<Claim> GetAllClaims()
    {
        return User?.Claims ?? Enumerable.Empty<Claim>();
    }

    public bool HasClaim(string claimType, string? claimValue = null)
    {
        if (User == null)
            return false;

        return claimValue == null
            ? User.HasClaim(c => c.Type == claimType)
            : User.HasClaim(claimType, claimValue);
    }

    public string? GetIpAddress()
    {
        // X-Forwarded-For is resolved by UseTrustedForwardedHeaders (trusted proxies only) into
        // Connection.RemoteIpAddress. Reading the raw header here would let any client spoof its IP.
        return Context == null ? null : ClientIp.GetRemoteIpString(Context);
    }

    public string? GetUserAgent()
    {
        return Context?.Request.Headers["User-Agent"].FirstOrDefault();
    }

    public string? GetRequestPath()
    {
        return Context?.Request.Path.Value;
    }

    public string? GetRequestMethod()
    {
        return Context?.Request.Method;
    }

    public string? GetCorrelationId()
    {
        if (Context == null)
            return null;

        return Context.Items.TryGetValue(CorrelationExtensions.ItemKey, out var id) && id is string s
            ? s
            : GetHeaderValue(CorrelationIdHeaderKey) ?? Context.TraceIdentifier;
    }

    public string? GetHeaderValue(string headerKey)
    {
        return Context?.Request.Headers[headerKey].FirstOrDefault();
    }

    public string? GetHost()
    {
        return Context?.Request.Host.Value;
    }

    public string? GetScheme()
    {
        return Context?.Request.Scheme;
    }

    public string? GetRequestUrl()
    {
        if (Context == null)
            return null;

        var request = Context.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
    }

    public string? GetTenantId()
    {
        // The RESOLVED tenant (validated by TenantResolutionMiddleware: token claim, host or header + membership).
        // Never read the raw header here - any client can send it.
        return Context?.RequestServices.GetService<ITenantContext>()?.TenantId?.ToString();
    }
}
