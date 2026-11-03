using System.Security.Claims;

namespace Shared.Core.Web;

/// <summary>
/// Service for accessing HTTP context information including user claims, IP address, and request metadata
/// </summary>
public interface IHttpContextService
{
    /// <summary>
    /// Gets the current user's ID from NameIdentifier claim
    /// </summary>
    string? GetUserId();
    
    /// <summary>
    /// Gets the current user's ID as a long, returns null if not found or invalid
    /// </summary>
    long? GetUserIdAsLong();
    
    /// <summary>
    /// Gets the current user's ID as a Guid, returns null if not found or invalid
    /// </summary>
    Guid? GetUserIdAsGuid();
    
    /// <summary>
    /// Gets the current user's username from Name claim
    /// </summary>
    string? GetUserName();
    
    /// <summary>
    /// Gets the current user's email from Email claim
    /// </summary>
    string? GetEmail();
    
    /// <summary>
    /// Gets the current user's roles
    /// </summary>
    IEnumerable<string> GetRoles();
    
    /// <summary>
    /// Checks if the user is authenticated
    /// </summary>
    bool IsAuthenticated();
    
    /// <summary>
    /// Gets a specific claim value by claim type
    /// </summary>
    string? GetClaimValue(string claimType);
    
    /// <summary>
    /// Gets all claim values for a specific claim type
    /// </summary>
    IEnumerable<string> GetClaimValues(string claimType);
    
    /// <summary>
    /// Gets all claims for the current user
    /// </summary>
    IEnumerable<Claim> GetAllClaims();
    
    /// <summary>
    /// Checks if the user has a specific claim
    /// </summary>
    bool HasClaim(string claimType, string? claimValue = null);
    
    /// <summary>
    /// Gets the client's IP address
    /// </summary>
    string? GetIpAddress();
    
    /// <summary>
    /// Gets the User-Agent header value
    /// </summary>
    string? GetUserAgent();
    
    /// <summary>
    /// Gets the request path
    /// </summary>
    string? GetRequestPath();
    
    /// <summary>
    /// Gets the request method (GET, POST, etc.)
    /// </summary>
    string? GetRequestMethod();
    
    /// <summary>
    /// Gets the correlation ID from the request
    /// </summary>
    string? GetCorrelationId();
    
    /// <summary>
    /// Gets a header value by key
    /// </summary>
    string? GetHeaderValue(string headerKey);
    
    /// <summary>
    /// Gets the host name from the request
    /// </summary>
    string? GetHost();
    
    /// <summary>
    /// Gets the request scheme (http, https)
    /// </summary>
    string? GetScheme();
    
    /// <summary>
    /// Gets the full request URL
    /// </summary>
    string? GetRequestUrl();
    
    /// <summary>
    /// Gets the RESOLVED current tenant id (see <c>ITenantContext</c>); <c>null</c> when no tenant is resolved.
    /// </summary>
    string? GetTenantId();
}
