namespace Shared.Core.Web;

/// <summary>
/// Extension methods for IHttpContextService to provide additional convenience methods
/// </summary>
public static class HttpContextServiceExtensions
{
    /// <summary>
    /// Gets the current user's full display name (tries name, username, email in order)
    /// </summary>
    public static string GetUserDisplayName(this IHttpContextService service)
    {
        return service.GetUserName() 
               ?? service.GetEmail() 
               ?? service.GetUserId() 
               ?? "Anonymous";
    }
    
    /// <summary>
    /// Checks if the current user has any of the specified roles
    /// </summary>
    public static bool HasAnyRole(this IHttpContextService service, params string[] roles)
    {
        if (roles == null || roles.Length == 0) return false;
        
        var userRoles = service.GetRoles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles.Any(role => userRoles.Contains(role));
    }
    
    /// <summary>
    /// Checks if the current user has all of the specified roles
    /// </summary>
    public static bool HasAllRoles(this IHttpContextService service, params string[] roles)
    {
        if (roles == null || roles.Length == 0) return true;
        
        var userRoles = service.GetRoles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles.All(role => userRoles.Contains(role));
    }
    
    /// <summary>
    /// Gets audit information for the current user
    /// </summary>
    public static AuditInfo GetAuditInfo(this IHttpContextService service)
    {
        return new AuditInfo
        {
            UserId = service.GetUserId(),
            UserName = service.GetUserName(),
            Email = service.GetEmail(),
            IpAddress = service.GetIpAddress(),
            UserAgent = service.GetUserAgent(),
            Timestamp = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Gets request context information
    /// </summary>
    public static RequestContext GetRequestContext(this IHttpContextService service)
    {
        return new RequestContext
        {
            Method = service.GetRequestMethod(),
            Path = service.GetRequestPath(),
            Url = service.GetRequestUrl(),
            IpAddress = service.GetIpAddress(),
            UserAgent = service.GetUserAgent(),
            CorrelationId = service.GetCorrelationId(),
            Host = service.GetHost(),
            Scheme = service.GetScheme()
        };
    }
    
    /// <summary>
    /// Checks if the request is from a specific IP address or IP range
    /// </summary>
    public static bool IsFromIpAddress(this IHttpContextService service, string ipAddress)
    {
        var currentIp = service.GetIpAddress();
        return currentIp != null && currentIp.Equals(ipAddress, StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Checks if the request is HTTPS
    /// </summary>
    public static bool IsHttps(this IHttpContextService service)
    {
        return service.GetScheme()?.Equals("https", StringComparison.OrdinalIgnoreCase) ?? false;
    }
}

public class AuditInfo
{
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
}

public class RequestContext
{
    public string? Method { get; set; }
    public string? Path { get; set; }
    public string? Url { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public string? Host { get; set; }
    public string? Scheme { get; set; }
}
