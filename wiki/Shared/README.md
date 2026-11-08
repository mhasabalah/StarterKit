# HttpContextService

Production-ready service for accessing HTTP context, user claims, and request metadata in ASP.NET Core applications.

## Quick Start

### 1. Inject the Service
```csharp
public class MyHandler(IHttpContextService httpContextService) 
    : ICommandHandler<MyCommand, Result>
{
    public async Task<Result> Handle(MyCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserId();
        var userName = httpContextService.GetUserName();
        // ... use user context
    }
}
```

### 2. Check Authorization
```csharp
// Prefer endpoint policies (.RequirePermission / .RequireTenantPermission). In-handler checks are for row-level rules:
if (!httpContextService.HasAnyRole("Admin", "Manager"))
    throw new ForbiddenException("Insufficient permissions");
```

### 3. Get Audit Information
```csharp
var audit = httpContextService.GetAuditInfo();
logger.LogInformation("Action by {User} from {IP}", audit.UserId, audit.IpAddress);
```

## Features

✅ **User Identity**: Get user ID, name, email (string, long, or Guid formats)  
✅ **Claims & Roles**: Access user claims and check role membership  
✅ **IP Detection**: Smart IP address detection with proxy/load balancer support  
✅ **Request Metadata**: Path, method, headers, URL, user agent  
✅ **Audit Context**: Complete audit trail information  
✅ **Multi-Tenant**: Tenant ID from claims or headers  
✅ **Correlation Tracking**: X-Correlation-Id support  
✅ **Null-Safe**: Graceful handling of background jobs (no HTTP context)

## Most Common Methods

### User Identity
```csharp
var userId = httpContextService.GetUserId();              // string?
var userIdLong = httpContextService.GetUserIdAsLong();    // long?
var userIdGuid = httpContextService.GetUserIdAsGuid();    // Guid?
var userName = httpContextService.GetUserName();          // string?
var email = httpContextService.GetEmail();                // string?
var isAuth = httpContextService.IsAuthenticated();        // bool
```

### Roles & Claims
```csharp
var hasRole = httpContextService.HasAnyRole("Admin", "Manager");     // bool
var hasAllRoles = httpContextService.HasAllRoles("User", "Active");  // bool
var hasClaim = httpContextService.HasClaim("permission", "write");   // bool
var claimValue = httpContextService.GetClaimValue("custom_claim");   // string?
```

### Request Information
```csharp
var ipAddress = httpContextService.GetIpAddress();        // Handles X-Forwarded-For
var userAgent = httpContextService.GetUserAgent();
var path = httpContextService.GetRequestPath();
var method = httpContextService.GetRequestMethod();
var correlationId = httpContextService.GetCorrelationId();
```

### Extension Methods
```csharp
var displayName = httpContextService.GetUserDisplayName();  // Never null
var auditInfo = httpContextService.GetAuditInfo();         // Complete audit context
var requestContext = httpContextService.GetRequestContext(); // Request metadata
```

## Common Patterns

### Pattern 1: Authorization Check
```csharp
// Row-level ownership rule (endpoint already requires authentication). Catalog wraps this in IProductAuthorizationService.
public async Task<DeleteThingResult> Handle(DeleteThingCommand command, CancellationToken ct)
{
    var entity = await repository.GetByIdAsync(command.Id, ct) ?? throw new ThingNotFoundException(command.Id);

    var currentUserId = httpContextService.GetUserIdAsGuid();
    if (entity.OwnerId != currentUserId && !httpContextService.HasRole("Admin"))
        throw new ForbiddenException("You can only delete your own records.");

    // Proceed with deletion
}
```

### Pattern 2: Audit Logging
```csharp
public void LogAction(string action, string entityType, string entityId)
{
    var audit = httpContextService.GetAuditInfo();
    var request = httpContextService.GetRequestContext();
    
    logger.LogInformation(
        "Audit: {Action} on {EntityType}#{EntityId} by User={UserId} IP={IpAddress} Correlation={CorrelationId}",
        action, entityType, entityId, audit.UserId, audit.IpAddress, request.CorrelationId);
}
```

### Pattern 3: Tenant data - do NOT filter by hand

Never read a tenant id from the HTTP context to filter queries. The resolved tenant is `ITenantContext`
(`Shared.Core.MultiTenancy`), and tenant-data DbContexts apply the named `Tenant` filter and Postgres RLS
automatically. See [../MultiTenancy/Overview.md](../MultiTenancy/Overview.md).

### Pattern 4: Owner Check
```csharp
var currentUserId = httpContextService.GetUserId();
if (entity.OwnerId != currentUserId && !httpContextService.HasAnyRole("Admin"))
    throw new ForbiddenException();
```

## Automatic Auditing

The `AuditableEntityInterceptor` automatically uses `IHttpContextService` to populate audit fields:

```csharp
// No code needed! These are set automatically:
entity.CreatedBy = httpContextService.GetUserId() ?? "system";
entity.CreatedAt = DateTime.UtcNow;
entity.LastModifiedBy = httpContextService.GetUserId() ?? "system";
entity.LastModified = DateTime.UtcNow;
```

**Fallback chain**: User ID → Username → Email → "authenticated-user" → "system"

## IP Address Detection

Smart IP detection with proxy/load balancer support:

```csharp
var ipAddress = httpContextService.GetIpAddress();
```

**Checks in order**:
1. `X-Forwarded-For` header (proxy/CDN)
2. `X-Real-IP` header (nginx, load balancer)
3. `RemoteIpAddress` (direct connection)

## Multi-Tenant Support

```csharp
var tenantId = httpContextService.GetTenantId();  // From claim or X-Tenant-Id header
if (!string.IsNullOrEmpty(tenantId))
{
    // Apply tenant filtering
}
```

## Testing

### Mock Setup
```csharp
var mockService = new Mock<IHttpContextService>();
mockService.Setup(x => x.GetUserId()).Returns("user-123");
mockService.Setup(x => x.GetUserName()).Returns("testuser");
mockService.Setup(x => x.IsAuthenticated()).Returns(true);
mockService.Setup(x => x.HasAnyRole("Admin")).Returns(true);
mockService.Setup(x => x.GetIpAddress()).Returns("192.168.1.100");

var handler = new MyHandler(repository, mockService.Object);
```

<!--#if (sample)-->
For test doubles, see `TestSupport.Http(userId, roles)` in `src/Modules/Catalog/Catalog.Tests/TestSupport.cs` and `FakeHttpContextService` in `Catalog.IntegrationTests`.
<!--#endif-->

## Background Jobs

Service gracefully handles missing HTTP context:

```csharp
public void ProcessBackgroundJob()
{
    var userId = httpContextService.GetUserId() ?? "system";
    
    if (!httpContextService.IsAuthenticated())
    {
        // Running in background, no user context
    }
}
```

All methods return nullable values - always check for null in background operations.

## Best Practices

1. **Always inject as scoped** - Service depends on `IHttpContextAccessor` (per-request)
2. **Null checks** - All methods return nullable values
3. **Background jobs** - Service returns null gracefully when no HTTP context
4. **Multi-tenancy** - Use `GetTenantId()` for tenant-aware operations
5. **Audit trails** - Use `GetAuditInfo()` for complete context
6. **IP detection** - Service handles proxy headers automatically

## vs ICurrentUserProvider

| Feature | IHttpContextService | ICurrentUserProvider |
|---------|-------------------|---------------------|
| User ID formats | ✅ String, Long, Guid | Long only |
| Claims access | ✅ Full | ❌ None |
| Roles | ✅ Yes | ❌ No |
| IP Address | ✅ Yes (proxy-aware) | ❌ No |
| Request metadata | ✅ Yes | ❌ No |
| Audit info | ✅ Yes | ❌ No |
| Multi-tenant | ✅ Yes | ❌ No |

**Recommendation**: Use `IHttpContextService` for all new features. It's comprehensive, production-ready, and handles all edge cases.

## Related Documentation

<!--#if (sample)-->
- [Catalog tests](../../src/Modules/Catalog/Catalog.Tests/TestSupport.cs) - how to fake the current user in tests
<!--#endif-->
- [Notification module](../Modules/Notification.md) - email + WhatsApp delivery, templates, retries, OTP and admin APIs
- Source: `src/Shared/Shared.Core/Web/IHttpContextService.cs`
- Implementation: `src/Shared/Shared.Core/Web/HttpContextService.cs`
- Extensions: `src/Shared/Shared.Core/Web/HttpContextServiceExtensions.cs`
