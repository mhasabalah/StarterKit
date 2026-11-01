using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Shared.Core.Web;

public class HttpContextService : IHttpContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string TenantIdClaimType = "tenant_id";
    private const string TenantIdHeaderKey = "X-Tenant-Id";
    private const string CorrelationIdHeaderKey = "X-Correlation-Id";

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
        if (User == null) return Enumerable.Empty<string>();
        
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
        if (User == null) return Enumerable.Empty<string>();
        
        return User.FindAll(claimType).Select(c => c.Value);
    }

    public IEnumerable<Claim> GetAllClaims()
    {
        return User?.Claims ?? Enumerable.Empty<Claim>();
    }

    public bool HasClaim(string claimType, string? claimValue = null)
    {
        if (User == null) return false;
        
        return claimValue == null 
            ? User.HasClaim(c => c.Type == claimType) 
            : User.HasClaim(claimType, claimValue);
    }

    public string? GetIpAddress()
    {
        if (Context == null) return null;

        // Check for forwarded IP (behind proxy/load balancer)
        var forwardedFor = Context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }

        // Check for real IP header
        var realIp = Context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fallback to connection remote IP
        return Context.Connection.RemoteIpAddress?.ToString();
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
        return GetHeaderValue(CorrelationIdHeaderKey) 
               ?? Context?.TraceIdentifier;
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
        if (Context == null) return null;

        var request = Context.Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
    }

    public string? GetTenantId()
    {
        // Try to get from claims first
        var tenantIdFromClaim = GetClaimValue(TenantIdClaimType);
        if (!string.IsNullOrEmpty(tenantIdFromClaim))
        {
            return tenantIdFromClaim;
        }

        // Fallback to header
        return GetHeaderValue(TenantIdHeaderKey);
    }
}
